using System.ComponentModel;
using System.Configuration.Install;

namespace HTTPSrvLauncher
{
    [RunInstaller(true)]
    public class ProjectInstaller : Installer
    {
        public ProjectInstaller() => InitializeComponent();
        
        private System.ComponentModel.IContainer components = null;

        private System.ServiceProcess.ServiceProcessInstaller serviceProcessInstaller1;
        private System.ServiceProcess.ServiceInstaller serviceInstaller1;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.serviceProcessInstaller1 = new System.ServiceProcess.ServiceProcessInstaller();
            this.serviceInstaller1 = new System.ServiceProcess.ServiceInstaller();

            this.serviceProcessInstaller1.Account = System.ServiceProcess.ServiceAccount.LocalSystem;
            this.serviceProcessInstaller1.Password = null;
            this.serviceProcessInstaller1.Username = null;

            this.serviceInstaller1.ServiceName = "HTTPSrvLauncher";
            this.serviceInstaller1.DisplayName = "HTTP Server Launcher";
            this.serviceInstaller1.Description = "dkxce starter (on-demand) for 3rd-party console-servers";

            this.serviceInstaller1.StartType = System.ServiceProcess.ServiceStartMode.Automatic;

            this.Installers.AddRange(new System.Configuration.Install.Installer[] {
                this.serviceProcessInstaller1,
                this.serviceInstaller1
            });
        }
    }
}