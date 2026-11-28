{ ... }:
{
  boot.initrd.availableKernelModules = [
    "vmw_pvscsi"
    "mptspi"
    "ata_piix"
    "sd_mod"
    "sr_mod"
    "e1000"
  ];
  boot.loader.grub = {
    enable = true;
    efiSupport = true;
    efiInstallAsRemovable = true;
    device = "/dev/sda";
  };

  virtualisation.vmware.guest.enable = true;

  networking = {
    useDHCP = false;
    interfaces.ens32.ipv4.addresses = [{
      address = "163.5.53.47";
      prefixLength = 24;
    }];
    defaultGateway = {
      address = "163.5.53.1";
      interface = "ens32";
    };
    nameservers = [ "8.8.8.8" "8.8.4.4" ];
  };
}
