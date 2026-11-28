{ pkgs, ... }:
let
  alloyConfig = pkgs.writeText "config.alloy" ''
    local.file_match "fetih" {
      path_targets = [
        {"__path__" = "/var/lib/fetih/logs/*/*/*.jsonl", "__path_exclude__" = "**/*-audit-*.jsonl"},
      ]
      sync_period = "10s"
    }

    loki.source.file "fetih" {
      targets    = local.file_match.fetih.targets
      forward_to = [loki.process.fetih.receiver]
    }

    loki.process "fetih" {
      stage.json {
        expressions = {
          server = "server",
          level  = "level",
          event  = "event",
          ts     = "ts",
        }
      }

      stage.regex {
        source     = "filename"
        expression = "^/var/lib/fetih/logs/(?P<env>[^/]+)/"
      }

      stage.labels {
        values = {
          env    = "",
          server = "",
          level  = "",
          event  = "",
        }
      }

      stage.timestamp {
        source = "ts"
        format = "RFC3339Nano"
      }

      stage.label_drop {
        values = ["filename"]
      }

      forward_to = [loki.write.local.receiver]
    }

    loki.write "local" {
      endpoint {
        url = "http://127.0.0.1:3100/loki/api/v1/push"
      }
    }
  '';
in
{
  services.loki = {
    enable = true;
    dataDir = "/var/lib/fetih/loki";
    configuration = {
      auth_enabled = false;
      server = {
        http_listen_address = "127.0.0.1";
        http_listen_port = 3100;
        grpc_listen_address = "127.0.0.1";
        log_level = "warn";
      };
      common = {
        instance_addr = "127.0.0.1";
        path_prefix = "/var/lib/fetih/loki";
        replication_factor = 1;
        ring.kvstore.store = "inmemory";
        storage.filesystem = {
          chunks_directory = "/var/lib/fetih/loki/chunks";
          rules_directory = "/var/lib/fetih/loki/rules";
        };
      };
      schema_config.configs = [{
        from = "2026-01-01";
        store = "tsdb";
        object_store = "filesystem";
        schema = "v13";
        index = {
          prefix = "index_";
          period = "24h";
        };
      }];
      limits_config = {
        retention_period = "2160h";
        retention_stream = [
          { selector = ''{env="test"}''; priority = 100; period = "168h"; }
          { selector = ''{event=~"trade\\..+|booth\\.sold|gm\\..+|web\\..+|character\\.deleted|session\\.cheat_report|pk\\.jailed|account\\.created"}''; priority = 50; period = "87600h"; }
          { selector = ''{event=~"session\\..+|account\\.login.*"}''; priority = 40; period = "17520h"; }
          { selector = ''{event=~"(move|combat|monster)\\..+"}''; priority = 20; period = "168h"; }
          { selector = ''{event=~"packet\\..+"}''; priority = 10; period = "48h"; }
        ];
        reject_old_samples_max_age = "720h";
        ingestion_rate_mb = 4;
        ingestion_burst_size_mb = 8;
        per_stream_rate_limit = "4MB";
        per_stream_rate_limit_burst = "8MB";
        max_query_series = 5000;
        allow_structured_metadata = true;
        discover_service_name = [];
        discover_log_levels = false;
      };
      compactor = {
        working_directory = "/var/lib/fetih/loki/compactor";
        compaction_interval = "10m";
        retention_enabled = true;
        retention_delete_delay = "2h";
        retention_delete_worker_count = 150;
        delete_request_store = "filesystem";
      };
      analytics.reporting_enabled = false;
    };
  };

  services.alloy = {
    enable = true;
    extraFlags = [ "--disable-reporting" ];
  };
  environment.etc."alloy/config.alloy".source = alloyConfig;
  systemd.services.alloy.serviceConfig = {
    DynamicUser = pkgs.lib.mkForce false;
    SupplementaryGroups = [ "fetih-logs" ];
    ReadOnlyPaths = [ "/var/lib/fetih/logs" ];
  };

  services.prometheus = {
    enable = true;
    listenAddress = "127.0.0.1";
    port = 9090;
    stateDir = "prometheus";
    retentionTime = "30d";
    scrapeConfigs = [
      {
        job_name = "game";
        static_configs = [
          { targets = [ "127.0.0.1:9818" ]; labels = { server = "game"; env = "prod"; }; }
          { targets = [ "127.0.0.1:9828" ]; labels = { server = "game"; env = "test"; }; }
        ];
      }
      {
        job_name = "account";
        static_configs = [
          { targets = [ "127.0.0.1:9819" ]; labels = { server = "account"; env = "shared"; }; }
        ];
      }
      {
        job_name = "node";
        static_configs = [
          { targets = [ "127.0.0.1:9100" ]; }
        ];
      }
    ];
    exporters.node = {
      enable = true;
      listenAddress = "127.0.0.1";
      port = 9100;
      extraFlags = [
        "--collector.textfile.directory=/var/lib/fetih/prometheus/textfile"
        "--collector.filesystem.mount-points-exclude=^/(boot|dev|proc|run|sys)(/.*)?$"
      ];
    };
  };
  # Keep Prometheus TSDB inside /var/lib/fetih/prometheus
  systemd.services.prometheus.serviceConfig.StateDirectory = pkgs.lib.mkForce "";
  systemd.services.prometheus.serviceConfig.WorkingDirectory = pkgs.lib.mkForce "/var/lib/fetih/prometheus";
  systemd.services.prometheus.serviceConfig.ReadWritePaths = [ "/var/lib/fetih/prometheus" ];
  systemd.services.prometheus.preStart = ''
    install -d -m 0750 -o prometheus -g prometheus /var/lib/fetih/prometheus
  '';
  services.prometheus.extraFlags = [ "--storage.tsdb.path=/var/lib/fetih/prometheus" ];

  services.grafana = {
    enable = true;
    dataDir = "/var/lib/fetih/grafana";
    settings = {
      server = {
        http_addr = "127.0.0.1";
        http_port = 3000;
        domain = "fetihoyun.com";
        root_url = "https://fetihoyun.com/grafana/";
        serve_from_sub_path = true;
      };
      security = {
        admin_password = "$__file{/run/credentials/grafana.service/admin-password}";
        secret_key = "$__file{/run/credentials/grafana.service/secret-key}";
        allow_embedding = false;
      };
      "auth.anonymous".enabled = false;
      "auth.proxy" = {
        enabled = true;
        header_name = "X-Webauth-User";
        header_property = "username";
        auto_sign_up = true;
        sync_ttl = 60;
      };
      users = {
        auto_assign_org = true;
        auto_assign_org_role = "Viewer";
      };
      analytics = {
        reporting_enabled = false;
        check_for_updates = false;
      };
      dashboards.default_home_dashboard_path = "/etc/grafana/dashboards/overview.json";
    };
    provision = {
      enable = true;
      datasources.settings = {
        apiVersion = 1;
        datasources = [
          {
            name = "Loki";
            uid = "loki";
            type = "loki";
            access = "proxy";
            url = "http://127.0.0.1:3100";
            isDefault = true;
            jsonData = {
              maxLines = 5000;
              derivedFields = [
                { name = "Player (by account)"; matcherRegex = ''"accountId":(\d+)''; url = "/admin/players/$${__value.raw}"; }
                { name = "Player (by character)"; matcherRegex = ''"characterId":(\d+)''; url = "/admin/characters/$${__value.raw}"; }
                { name = "Item history"; matcherRegex = ''"itemId":(\d+)''; url = "/grafana/d/fetih-items/items?var-itemId=$${__value.raw}"; }
              ];
            };
          }
          {
            name = "Prometheus";
            uid = "prometheus";
            type = "prometheus";
            access = "proxy";
            url = "http://127.0.0.1:9090";
            jsonData.timeInterval = "15s";
          }
          {
            name = "MariaDB";
            uid = "mysql";
            type = "mysql";
            access = "proxy";
            url = "/run/mysqld/mysqld.sock";
            user = "grafana";
            jsonData = {
              database = "fetih_game";
              maxOpenConns = 10;
              maxIdleConns = 2;
              connMaxLifetime = 14400;
            };
          }
        ];
      };
      dashboards.settings = {
        apiVersion = 1;
        providers = [{
          name = "Fetih";
          orgId = 1;
          type = "file";
          disableDeletion = true;
          updateIntervalSeconds = 30;
          allowUiUpdates = false;
          options.path = "/etc/grafana/dashboards";
        }];
      };
      alerting.policies.path = ../../observability/grafana/provisioning/alerting/policies.yaml;
      alerting.rules.path = ../../observability/grafana/provisioning/alerting/rules.yaml;
      alerting.contactPoints.settings = {
        apiVersion = 1;
        contactPoints = [{
          orgId = 1;
          name = "discord";
          receivers = [{
            uid = "discord_webhook";
            type = "discord";
            disableResolveMessage = false;
            settings = {
              url = "$__file{/run/credentials/grafana.service/discord-webhook}";
              use_discord_username = false;
            };
          }];
        }];
      };
    };
  };

  environment.etc."grafana/dashboards".source = ../../observability/grafana/dashboards;
  systemd.services.grafana = {
    after = [ "fetih-host-secrets.service" "mysql.service" ];
    unitConfig.ConditionPathExists = "/var/lib/fetih/secrets/discord-webhook";
    serviceConfig = {
      LoadCredential = [
        "admin-password:/var/lib/fetih/secrets/grafana-admin-password"
        "secret-key:/var/lib/fetih/secrets/grafana-secret-key"
        "discord-webhook:/var/lib/fetih/secrets/discord-webhook"
      ];
      ReadOnlyPaths = [ "/run/mysqld" ];
    };
  };
}
