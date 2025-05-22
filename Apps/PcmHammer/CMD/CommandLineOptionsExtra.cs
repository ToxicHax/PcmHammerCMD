using CommandLine;

namespace CMDVersion
{
    /// Options for commandline parameters
    /// using parser from:
    /// https://github.com/commandlineparser/commandline
    public class CommandLineOptionsExtra
    {
        [Option("dcat", Required = false, HelpText = "Device category (0-1), valid: 0 = Serial, 1 = j2534 device")]
        public string deviceCat { get; set; }

        [Option("dtype", Required = false, HelpText = "Device type (string), example: \"OBDLink\" | use list_devices to get a list of valid devices")]
        public string deviceType { get; set; }

        [Option("dcom", Required = false, HelpText = "Device Serial Com Port (0-256), valid: 0, 1, 2..")]
        public string deviceCom { get; set; }

        [Option("dlist", Required = false, HelpText = "Returns a list of valid devices")]
        public bool devicesList { get; set; }

        [Option("cmdversion", Required = false, HelpText = "Display version of CMD build (not PcmHammer version)")]
        public bool ShowCMDVersion { get; set; }

        [Option("hideconsole", Required = false, HelpText = "Hides the console window (WARNING: the program will run in the background until 'quit' has been sent through the pipe server, if you accidentally manually set this be ready to terminate the program yourself.)")]
        public bool HideConsole { get; set; }

    }
}