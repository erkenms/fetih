{
  description = "Fetih Conquer Online 5619 server and NixOS host";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    disko = {
      url = "github:nix-community/disko";
      inputs.nixpkgs.follows = "nixpkgs";
    };
  };

  outputs = { self, nixpkgs, disko }:
    let
      mkPackages = system:
        let
          pkgs = nixpkgs.legacyPackages.${system};
          mkDotnet = { pname, projectFile, runtime }:
            pkgs.buildDotnetModule {
              inherit pname projectFile;
              version = "0.1.0";
              src = ./.;
              nugetDeps = ./deploy/nix/deps.json;
              dotnet-sdk = pkgs.dotnetCorePackages.sdk_11_0;
              dotnet-runtime = runtime;
              dotnetFlags = [ "-p:TargetFramework=net11.0" ];
              selfContainedBuild = false;
            };
        in
        {
          fetih-account = mkDotnet {
            pname = "Fetih.Account";
            projectFile = "src/Fetih.Account/Fetih.Account.csproj";
            runtime = pkgs.dotnetCorePackages.aspnetcore_11_0;
          };
          fetih-game = mkDotnet {
            pname = "Fetih.Game";
            projectFile = "src/Fetih.Game/Fetih.Game.csproj";
            runtime = pkgs.dotnetCorePackages.aspnetcore_11_0;
          };
          fetih-web = mkDotnet {
            pname = "Fetih.Web";
            projectFile = "src/Fetih.Web/Fetih.Web.csproj";
            runtime = pkgs.dotnetCorePackages.aspnetcore_11_0;
          };
        };
    in
    {
      packages.x86_64-linux = mkPackages "x86_64-linux";
      packages.aarch64-linux = mkPackages "aarch64-linux";

      nixosConfigurations.fetih = nixpkgs.lib.nixosSystem {
        system = "x86_64-linux";
        specialArgs = { inherit self; };
        modules = [
          disko.nixosModules.disko
          ./deploy/nix/disko.nix
          ./deploy/nix/hardware.nix
          ./deploy/nix/configuration.nix
          ./deploy/nix/database.nix
          ./deploy/nix/services.nix
          ./deploy/nix/observability.nix
          ./deploy/nix/backups.nix
          ./deploy/nix/deploy.nix
        ];
      };
    };
}
