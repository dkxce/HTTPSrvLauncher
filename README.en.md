# HTTPSrvLauncher

[RU](README.md) | [EN](README.en.md)

`HTTPSrvLauncher`It is a lightweight automation tool (Windows service) that listens to specified network ports and automatically launches original server applications (e.g.`TorrServer`, `Ace Stream`or any other web servers) only at the time of the first incoming request.

This allows you to save system resources (RAM and CPU), without keeping servers constantly running in the background.

## Major opportunities

* **Launch on demand (On-Demand):**The server sleeps until no one accesses it.
* **Support for any servers:**Works with`TorrServer`Media servers and other CLI applications.
* **Multiportability:**The ability to listen to multiple ports for different utilities.
* **Autocompletion:**Optional shutdown of the target application in the absence of activity (timeout).
* **Windows Service:**It works in the background as a standard Windows Service.

## Installation and management of the service

The project is designed**C#**. You can collect it from the source or use a ready-made binary file.

To manage Windows-service, use the utility`sc.exe`In the command line on behalf of the Administrator:

### Registration of service

```
sc create HTTPSrvLauncher binPath= "C:\Path\To\HTTPSrvLauncher.exe" start= auto
ИЛИ
install.cmd
```

### Start-up of the service

```
sc start HTTPSrvLauncher
ИЛИ
net start HTTPSrvLauncher
ИЛИ
start.cmd
```

### Stoppage of service

```
sc stop HTTPSrvLauncher
ИЛИ
net stop HTTPSrvLauncher
ИЛИ
stop.cmd
```

### Removal of service

```
sc delete HTTPSrvLauncher
ИЛИ
uninstall.cmd
```

## Setting (Configuration)

Configuring the start parameters is carried out through the configuration file`HTTPSrvLauncher.json`located in the directory with the service.

Example of a file`HTTPSrvLauncher.json`:

```
{   
    "8090": {
        "ServerPath": "C:\\DISK4\\TorrServer\\TorrServer-windows-amd64.exe",
        "SendWebRedirect": true,
        "Arguments": "--path \"C:\\DISK4\\TorrServer\""
    }
}
```

## Principle of work

After the start of Windows service`HTTPSrvLauncher`It occupies these ports. As soon as your player or browser makes a request`http://localhost:8090`The launcher will instantly start the target process (e.g.`TorrServer`) and redirect network traffic.