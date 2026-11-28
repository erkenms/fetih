{ pkgs, ... }:
let
  pins = import ./client-base.nix;
  r2 = import ./r2.nix;
  repoUrl = "https://github.com/erkenms/fetih.git";
in
{
  systemd.services.fetih-client-base = {
    description = "Fetch and verify pinned client base archives from R2";
    wants = [ "network-online.target" ];
    after = [ "network-online.target" ];
    unitConfig.ConditionPathExists = "/var/lib/fetih/secrets/r2.env";
    serviceConfig = {
      Type = "oneshot";
      LoadCredential = "r2:/var/lib/fetih/secrets/r2.env";
      PrivateTmp = true;
    };
    script = ''
      set -euo pipefail
      source /run/credentials/fetih-client-base.service/r2
      export R2_ENDPOINT="${r2.endpoint}"
      export R2_BUCKET="${r2.bucket}"
      export RCLONE_CONFIG_R2_TYPE=s3
      export RCLONE_CONFIG_R2_PROVIDER=Cloudflare
      export RCLONE_CONFIG_R2_ACCESS_KEY_ID="$R2_ACCESS_KEY_ID"
      export RCLONE_CONFIG_R2_SECRET_ACCESS_KEY="$R2_SECRET_ACCESS_KEY"
      export RCLONE_CONFIG_R2_ENDPOINT="$R2_ENDPOINT"
      export RCLONE_CONFIG_R2_REGION=auto
      export RCLONE_CONFIG_R2_NO_CHECK_BUCKET=true

      fetch_one() {
        local name="$1"
        local expected="$2"
        local dest="/var/lib/fetih/client/base/$name"
        local stamp="$dest/.sha256"
        if [ -f "$stamp" ] && [ "$(cat "$stamp")" = "$expected" ]; then
          return 0
        fi
        local tmp="$(mktemp -d)"
        ${pkgs.rclone}/bin/rclone copyto "r2:$R2_BUCKET/client-base/$name.tar.zst" "$tmp/$name.tar.zst"
        echo "$expected  $tmp/$name.tar.zst" | ${pkgs.coreutils}/bin/sha256sum -c -
        rm -rf "$dest.new"
        mkdir -p "$dest.new"
        ${pkgs.zstd}/bin/zstd -dc "$tmp/$name.tar.zst" | ${pkgs.gnutar}/bin/tar -C "$dest.new" -xf -
        echo "$expected" > "$dest.new/.sha256"
        rm -rf "$dest.old"
        [ -d "$dest" ] && mv "$dest" "$dest.old" || true
        mv "$dest.new" "$dest"
        rm -rf "$dest.old" "$tmp"
      }

      fetch_one "co_5619" "${pins.co_5619}"
      fetch_one "fo_3105" "${pins.fo_3105}"
    '';
  };

  systemd.services.fetih-deploy = {
    description = "Self-deploy main (NixOS + test) and latest v* tag (prod + client)";
    wants = [ "network-online.target" "fetih-client-base.service" ];
    after = [ "network-online.target" "fetih-client-base.service" "mysql.service" ];
    path = with pkgs; [
      git
      nix
      nixos-rebuild
      systemd
      dotnetCorePackages.sdk_11_0
      coreutils
      gnugrep
      gawk
    ];
    serviceConfig = {
      Type = "oneshot";
      Environment = [
        "DOTNET_CLI_TELEMETRY_OPTOUT=1"
        "LD_LIBRARY_PATH=${pkgs.lib.makeLibraryPath [ pkgs.stdenv.cc.cc.lib pkgs.zlib pkgs.fontconfig pkgs.freetype ]}"
      ];
    };
    script = ''
      set -euo pipefail
      export PATH="/run/current-system/sw/bin:$PATH"

      REPOS="/var/lib/fetih/repos"
      DEPLOY="/var/lib/fetih/deploy"
      STUDIO="/var/lib/fetih/studio"
      STATE="$DEPLOY/state"
      mkdir -p "$REPOS" "$DEPLOY" "$STATE"

      # 1. Fetch main + tags
      if [ ! -d "$REPOS/main/.git" ]; then
        git clone --branch main "${repoUrl}" "$REPOS/main"
      else
        git -C "$REPOS/main" fetch --prune --tags origin main
      fi

      MAIN_REV="$(git -C "$REPOS/main" rev-parse origin/main)"
      PREV_MAIN="$(cat "$STATE/main.rev" 2>/dev/null || true)"

      if [ "$MAIN_REV" != "$PREV_MAIN" ]; then
        git -C "$REPOS/main" checkout -f "$MAIN_REV"

        # Apply NixOS configuration from main; failed switch leaves previous generation running
        nixos-rebuild switch --flake "$REPOS/main#fetih" --no-write-lock-file

        # Reset Studio writable checkout to origin/main
        if [ ! -d "$STUDIO/.git" ]; then
          git clone --branch main "${repoUrl}" "$STUDIO"
        else
          git -C "$STUDIO" fetch --prune origin main
        fi
        git -C "$STUDIO" reset --hard "$MAIN_REV"
        git -C "$STUDIO" clean -fd src/Fetih.Game/Data
        chown -R fetih-test-web:fetih-test "$STUDIO"
        chmod -R g+rX "$STUDIO"

        # Build test services from main
        nix build "$REPOS/main#fetih-game" --out-link "$DEPLOY/test-game.new" --no-write-lock-file
        nix build "$REPOS/main#fetih-web" --out-link "$DEPLOY/test-web.new" --no-write-lock-file
        mv -Tf "$DEPLOY/test-game.new" "$DEPLOY/test-game"
        mv -Tf "$DEPLOY/test-web.new" "$DEPLOY/test-web"

        systemctl restart fetih-test-game fetih-test-web
        systemctl start fetih-db-grants.service || true
        echo "$MAIN_REV" > "$STATE/main.rev"
      fi

      # 2. Check latest v* tag for production + client
      LATEST_TAG="$(git -C "$REPOS/main" tag -l 'v*' --sort=-v:refname | head -n1 || true)"
      PREV_TAG="$(cat "$STATE/prod.tag" 2>/dev/null || true)"

      if [ -n "$LATEST_TAG" ] && [ "$LATEST_TAG" != "$PREV_TAG" ]; then
        if [ ! -d "$REPOS/prod/.git" ]; then
          git clone --tags "${repoUrl}" "$REPOS/prod"
        else
          git -C "$REPOS/prod" fetch --prune --tags origin
        fi
        git -C "$REPOS/prod" checkout -f "tags/$LATEST_TAG"

        nix build "$REPOS/prod#fetih-account" --out-link "$DEPLOY/prod-account.new" --no-write-lock-file
        nix build "$REPOS/prod#fetih-game" --out-link "$DEPLOY/prod-game.new" --no-write-lock-file
        nix build "$REPOS/prod#fetih-web" --out-link "$DEPLOY/prod-web.new" --no-write-lock-file

        # Build client, low-zoom assets, and patches when base archives are present
        if [ -d /var/lib/fetih/client/base/co_5619 ] && [ -d /var/lib/fetih/client/base/fo_3105 ]; then
          (cd "$REPOS/prod" && dotnet run tools/build.cs all \
            --base /var/lib/fetih/client/base/co_5619 \
            --art /var/lib/fetih/client/base/fo_3105 \
            --out /var/lib/fetih/client/built \
            --assets /var/lib/fetih/client/assets \
            --patch /var/lib/fetih/client/patch \
            --ip 163.5.53.47 \
            --url https://fetihoyun.com)
        fi

        # Graceful countdown on production game server before switching
        if systemctl is-active --quiet fetih-prod-game.service && [ -x "$DEPLOY/prod-web/bin/Fetih.Web" ]; then
          if "$DEPLOY/prod-web/bin/Fetih.Web" shutdown-game 127.0.0.1 5817 60 "Sunucu güncelleniyor ($LATEST_TAG)"; then
            for _ in $(seq 1 95); do
              systemctl is-active --quiet fetih-prod-game.service || break
              sleep 1
            done
          fi
        fi

        mv -Tf "$DEPLOY/prod-account.new" "$DEPLOY/prod-account"
        mv -Tf "$DEPLOY/prod-game.new" "$DEPLOY/prod-game"
        mv -Tf "$DEPLOY/prod-web.new" "$DEPLOY/prod-web"

        systemctl restart fetih-account fetih-prod-game fetih-prod-web
        systemctl start fetih-db-grants.service || true
        echo "$LATEST_TAG" > "$STATE/prod.tag"
      fi
    '';
  };

  systemd.timers.fetih-deploy = {
    wantedBy = [ "timers.target" ];
    timerConfig = {
      OnBootSec = "30s";
      OnUnitActiveSec = "2m";
      Unit = "fetih-deploy.service";
    };
  };
}
