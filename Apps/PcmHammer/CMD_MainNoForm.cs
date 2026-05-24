using CommandLine;
using PcmHacking;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Messaging;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CMDVersion
{
    public partial class CMD_MainNoForm : MainForm, IPipeCallback
    {
        public string CMD_BUILD_VERSION = "0.1";

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();
        const int CMD_HIDE = 0;

        public string PATH_SAVE_PCM_IMAGE { get; set; }
        public string DEVICE_TYPE { get; private set; }
        public string DEVICE_CAT { get; private set; }
        public string DEVICE_COM_PORT { get; private set; }
        public string STATUS_ACTIVITY { get; private set; }
        public string STATUS_TIME_REMAINING { get; private set; }
        public string STATUS_PERCENT_DONE { get; private set; }
        public string STATUS_RETRIES { get; private set; }
        public string STATUS_TRANSFER_SPEED { get; private set; }
        public string STATUS_PCM_PROPS { get; private set; }

        public string LAST_CONSOLE_INPUT = null;

        /// <summary>
        /// set to true to use in development pcms that may not be fully supported yet, e.g. p05 pcm's
        /// </summary>
        public bool BYPASS_INDEV_PCM_SUPPORT = false;

        /// <summary>
        /// if the os id cannot be read from the pcm instead fall back to this os id.
        /// </summary>
        public uint FALLBACK_OPERATING_SYSTEM_ID = 0;

        public bool SHOW_DEBUG_MESSAGES = false;

        public bool HIDE_CONSOLE_WINDOW = false;

        public Dictionary<string, CMD_PipeCommand> pipe_commands = new Dictionary<string, CMD_PipeCommand>();
        private MemoryStream read_memoryStream;
        private string command_desc_readentire = "path=<file_path>\n-> Reads the PCM to the provided file path,\nexample: read_entire path=C:\\Users\\-USERNAME-\\CMDHammer\\output.bin";
        private string command_desc_readprops = "Reads only the OS ID, VIN...etc";
        private string command_desc_writeentire = "path=<file_path>\n-> Writes a full bin to the PCM from the provided file path,\nexample: write_entire path=C:\\Users\\-USERNAME-\\CMDHammer\\modded.bin";
        private string command_desc_getdevices = "\n-> Returns a list of device categories, types and com ports available.\nReturns device_list|category0,category1...|type0,type1,type2...|com0,com1,com2...";
        private string command_desc_setcurrdevice = "<device_cat>,<device_type>,<device_port>\n-> Expects a device category, type and com port, sets and saves it.";
        private string command_desc_testcurrdevice = "\n-> Checks if the current selected device is alive";

        public List<string> DeviceTypeList { get; private set; }
        public string CURRENT_VALID_DEVICE { get; private set; }

        public CMD_MainNoForm()
        {
            DeviceConfiguration.Settings.PropertyChanged += OnSettingsChanged;
            DeviceConfiguration.Settings.Reload();
            RegisterPipeCommand("read_entire", command_desc_readentire, command_Read_EntireAsync);
            RegisterPipeCommand("read_properties", command_desc_readprops, command_Read_PropertiesAsync);
            RegisterPipeCommand("write_pcm", command_desc_writeentire, command_Write_EntireAsync);
            RegisterPipeCommand("get_devices", command_desc_getdevices, command_GetDevicesAsync);
            RegisterPipeCommand("set_current_device", command_desc_setcurrdevice, command_SetCurrentDeviceAsync);
            RegisterPipeCommand("test_current_device", command_desc_testcurrdevice, command_TestCurrentDeviceAsync);

            ListAvailableCommands();

            //TODO: Use assembly to get all device types
            //List of available device types, too lazy to use assembly to get all types,
            //for now update manually when new devices get added
            DeviceTypeList = new List<string>();
            DeviceTypeList.Add(OBDXProDevice.DeviceType);
            DeviceTypeList.Add(AvtDevice.DeviceType);
            DeviceTypeList.Add(MockDevice.DeviceType);
            DeviceTypeList.Add(ElmDevice.DeviceType);

            AddLineBreakMessage("CMD PcmHammer version " + CMD_BUILD_VERSION, ConsoleColor.Cyan, ConsoleColor.Black);

            Task<CMD_PipeServer> task = Task.Run(() => new CMD_PipeServer(this));

            MainNoForm_Init();

            if (HIDE_CONSOLE_WINDOW)
            {
                var handle = GetConsoleWindow();
                ShowWindow(handle, CMD_HIDE); //5 = SHOW
            }
            else
            {
                //
                DoConsoleInput();

                AddUserMessage("Server is still running, WARNING: forcefully closing this window could cause damages to any PCM's currently being written to.");

                DoConsoleInput();

                // TODO: REQUEST SERVER THREAD TO EXIT AND SEE REPORT BACK WITH PROGRESS MAYBE INCASE ANY ON GOING WRITES OR READS ARE HAPPENING
                AddUserMessage("Requesting graceful exit from server... please wait. WARNING: forcefully closing this window could cause damages to any PCM's currently being written to.");
            }

            //and wait for on the pipe server for messages to get processed or getting a quit message to finish.
            task.Wait();
        }


        private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {

            DEVICE_CAT = DeviceConfiguration.Settings.DeviceCategory;

            if (DEVICE_CAT == "Serial")
                DEVICE_TYPE = DeviceConfiguration.Settings.SerialPortDeviceType;

            if (DEVICE_CAT == "J2534")
                DEVICE_TYPE = DeviceConfiguration.Settings.J2534DeviceType;

            DEVICE_COM_PORT = DeviceConfiguration.Settings.SerialPort;
        }

        public void UpdateTitleProgressBar(string state, int progress)
        {
            int prog = progress;
            string finalTitle = state + " [";
            for (int i = 0; i < 100; i++)
            {
                if (i == 50)
                {
                    finalTitle += "[ ";
                    if (progress < 10) finalTitle += " ";
                    if (progress < 99) finalTitle += " ";
                    finalTitle += progress + "%";
                    if (progress < 10) finalTitle += " ";
                    finalTitle += " ]";
                }
                finalTitle += (prog > i) ? "|" : ".";
            }
            finalTitle += "]";
            Console.Title = " | Status -> " + finalTitle;
        }

        private void ListAvailableCommands()
        {
            AddLineBreakMessage("CommandLine commands:", ConsoleColor.DarkGreen, ConsoleColor.White);

            Parser.Default.ParseArguments<CommandLineOptionsExtra>(new string[] { "--help" });

            AddLineBreakMessage("CMD PcmHammer Pipe Commands:", ConsoleColor.DarkBlue, ConsoleColor.White);

            foreach (CMD_PipeCommand command in pipe_commands.Values)
            {
                AddUserMessage("  ", false);

                if (command.description.Contains("\n"))
                {
                    bool f = true;
                    foreach (string line in command.description.Split('\n'))
                    {
                        var fline = line;
                        if (f)
                        {
                            fline = command.command + " " + line;
                            AddLineBreakMessage(fline, ConsoleColor.DarkRed, ConsoleColor.Yellow);
                        }
                        else
                        {
                            AddUserMessage(fline, false);
                        }

                        f = false;
                    }
                }
                else
                {
                    AddLineBreakMessage(command.command + " " + command.description, ConsoleColor.DarkRed, ConsoleColor.Yellow);
                }
            }

            AddUserMessage("   ", false);
            AddUserMessage("   ", false);
            AddLineBreakMessage("Example Usage: ", ConsoleColor.Black, ConsoleColor.Green);
            AddUserMessage("   ", false);
            AddUserMessage("--dlist (Displays all valid device categories, types and com ports)", false);
            AddUserMessage("   ", false);
            AddUserMessage("Pick the ones that match your setup", false);
            AddUserMessage("   ", false);
            AddUserMessage("example:", false);
            AddUserMessage("   ", false);

            AddUserMessage("--dcat Serial", false);
            AddUserMessage("--dtype ObdLink or AllPro", false);
            AddUserMessage("--dcom COM1", false);
            AddUserMessage("   ", false);
            AddUserMessage("Now you can run any pipe command or have another program send a pipe command.", false);
            AddUserMessage("example:", false);
            AddUserMessage("   ", false);
            AddUserMessage("read_entire path=C:\\Users\\-USERNAME-\\CMDHammer\\output.bin", false);
            AddUserMessage("   ", false);
            AddUserMessage("   ", false);
            AddUserMessage("write_pcm path=C:\\Users\\-USERNAME-\\CMDHammer\\bin-you-want-to-write.bin;None", false);
            AddUserMessage("(Replace 'None' with a valid write type: Full, Calibration, Compare, TestWrite, Parameters, OsPlusCalibrationPlusBoot)", false);
            AddUserMessage("   ", false);
        }

        private void DoConsoleInput()
        {
            bool running = true;
            while (running)
            {
                string input = Console.ReadLine();//.ToLower();
                LAST_CONSOLE_INPUT = input;
                if (LAST_CONSOLE_INPUT.Length > 0)
                {
                    if (input == "quit" || input == "exit")
                    {
                        running = false;
                    }
                    else
                    {
                        string replyData = "";
                        Task.Run(async () => { replyData = await ProcessPipeMessage(null, input.Replace(" ", "|")); }).Wait();
                        //TODO: process the reply data if needed, and make a little wrapper for the data to allow multiple data types, not just strings

                        if (replyData == null)
                        {
                            ProcessCommandLine();
                            if (LAST_CONSOLE_INPUT == null)
                            {
                                LAST_CONSOLE_INPUT = "--" + input; //little bypass for forgetful users (like myself)
                                ProcessCommandLine();
                            }

                            if (LAST_CONSOLE_INPUT == null)
                            {
                                LAST_CONSOLE_INPUT = input + " "; //little hack to check for empty commands
                                ProcessCommandLine();
                            }

                            if (LAST_CONSOLE_INPUT == null)
                            {
                                LAST_CONSOLE_INPUT = "--" + input + " "; //attempt bypass and empty command, there is a better non hacky way to do this but i cant be bothered.
                                ProcessCommandLine();
                            }

                            if (LAST_CONSOLE_INPUT == null)
                            {
                                if (input != "--help" && input != "--version")
                                    AddUserMessage("No valid CMD pipe command or CMD args found for '" + input + "'!");
                            }
                        }
                        else
                        {
                            AddUserMessage("ProcessPipeMessage Result: " + replyData);
                        }
                    }
                }
            }
        }
        private async Task<string> command_TestCurrentDeviceAsync(string payload, StreamWriter writer)
        {
            bool result = await this.ResetDevice();
            return "status_device|" + result;
        }

        public async Task<string> command_SetCurrentDeviceAsync(string payload, StreamWriter writer)
        {
            bool success = false;
            string[] parts = payload.Split(',');
            if (parts.Length >= 3)
            {
                string cat = parts[0].Trim();
                string type = parts[1].Trim();
                string port = parts[2].Trim();

                // Set type and port BEFORE category — OnSettingsChanged fires when DeviceCategory
                // changes and immediately reads SerialPortDeviceType/SerialPort back. If those
                // haven't been written yet they return stale/default values.
                DeviceConfiguration.Settings.J2534DeviceType = type;
                DeviceConfiguration.Settings.SerialPortDeviceType = type;
                DeviceConfiguration.Settings.SerialPort = port;
                DeviceConfiguration.Settings.DeviceCategory = cat;  // triggers OnSettingsChanged last
                DeviceConfiguration.Settings.Save();

                AddUserMessage($"Device set: {cat} / {type} / {port}");
                success = true;
            }
            else
            {
                AddUserMessage("set_current_device: expected payload 'category,type,port'");
            }

            return "set_current_device|" + success;
        }

        public async Task<string> command_GetDevicesAsync(string payload, StreamWriter writer)
        {
            string message = "device_list";//reply message with all the info
            message += "|";

            int i = 0;
            foreach (var constant in typeof(DeviceConfiguration.Constants).GetFields())
            {
                if (constant.IsLiteral && !constant.IsInitOnly)
                {
                    var msg = ((string)constant.GetValue(null));
                    if (i != 0) message += ",";
                    message += msg;
                    i++;
                }
            }
            message += "|";
            int c = 0;
            foreach (string dtyp in DeviceTypeList)
            {
                if (c != 0) message += ",";
                message += dtyp;
                c++;
            }

            int a = 0;
            foreach (J2534DotNet.J2534Device device in J2534DeviceFinder.FindInstalledJ2534DLLs(this))
            {
                if (a == 0 && c > 0) message += "|";
                if (a != 0) message += ",";
                message += device.Name;
                a++;
            }
            message += "|";
            int b = 0;
            foreach (object portInfo in PortDiscovery.GetPorts(this))
            {
                if (b != 0) message += ",";
                message += portInfo.ToString();
                b++;
            }

            return message;
        }

        private async Task<string> command_Read_PropertiesAsync(string payload, StreamWriter writer)
        {
            if (BackgroundWorker.IsAlive)
            {
                AddUserMessage("Error: Cannot read properties, Worker already busy with a task.");
                return "error_alreadybusy";
            }

            STATUS_PCM_PROPS = ""; // reset
            await ResetDevice(); // initialize this.Vehicle — without this it is null and the thread exits immediately
            BackgroundWorker = new Thread(() => readPropertiesButton_Thread());
            BackgroundWorker.IsBackground = true;
            BackgroundWorker.Start();

            // NOTE: readPropertiesButton_Thread is async void, so the thread itself exits
            // at the first await (IsAlive becomes false immediately). The actual queries
            // run as async continuations on the thread pool. We must poll STATUS_PCM_PROPS
            // for the "DONE" sentinel set at the end of the async method instead.
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (!STATUS_PCM_PROPS.EndsWith("DONE"))
            {
                if (timeout.Elapsed.TotalSeconds >= 20)
                {
                    AddUserMessage("read_properties: timed out waiting for ECU response.");
                    return "status_read_properties|timedout";
                }
                Thread.Sleep(100);
            }

            // Strip the trailing DONE sentinel and return exactly once.
            var result = "status_read_properties" + STATUS_PCM_PROPS.Replace("DONE", "");
            AddUserMessage("read_properties result: " + result);
            return result;
        }

        protected async void readPropertiesButton_Thread()
        {
            if (this.Vehicle == null)
            {
                // This shouldn't be possible - it would mean the buttons 
                // were enabled when they shouldn't be.
                return;
            }

            try
            {
                OSIDInfo pcmInfo = null;

                //this.DisableUserInput();

                var vinResponse = await this.Vehicle.QueryVin();
                if (vinResponse.Status != ResponseStatus.Success)
                {
                    this.AddUserMessage("VIN query failed: " + vinResponse.Status.ToString());
                    await this.Vehicle.ExitKernel();
                    STATUS_PCM_PROPS += "|FAILDONE";
                    return;
                }
                this.AddUserMessage("VIN: " + vinResponse.Value);
                STATUS_PCM_PROPS += "|" + vinResponse.Value;

                var osResponse = await this.Vehicle.QueryOperatingSystemId(CancellationToken.None);
                if (osResponse.Status == ResponseStatus.Success)
                {
                    this.AddUserMessage("OSID: " + osResponse.Value.ToString());
                    pcmInfo = new OSIDInfo(osResponse.Value);
                    this.AddUserMessage("Description: " + pcmInfo.Description);
                    STATUS_PCM_PROPS += "|" + osResponse.Value.ToString();

                }
                else
                {
                    this.AddUserMessage("OS ID query failed: " + osResponse.Status.ToString());
                    STATUS_PCM_PROPS += "|FAIL";
                }

                // Disable Calibration ID lookup for those that do not provide it
                if (pcmInfo != null && pcmInfo.HardwareType != PcmType.BlackBox)
                {

                    var calResponse = await this.Vehicle.QueryCalibrationId();
                    if (calResponse.Status == ResponseStatus.Success)
                    {
                        this.AddUserMessage("Calibration ID: " + calResponse.Value.ToString());
                        STATUS_PCM_PROPS += "|" + calResponse.Value.ToString();

                    }
                    else
                    {
                        this.AddUserMessage("Calibration ID query failed: " + calResponse.Status.ToString());
                        STATUS_PCM_PROPS += "|FAIL";
                    }
                }

                // Disable HardwareID lookup for the P10, P12 and E54.
                if (pcmInfo != null && pcmInfo.HardwareType != PcmType.P10 && pcmInfo.HardwareType != PcmType.P12 && pcmInfo.HardwareType != PcmType.E54)
                {
                    var hardwareResponse = await this.Vehicle.QueryHardwareId();
                    if (hardwareResponse.Status == ResponseStatus.Success)
                    {
                        this.AddUserMessage("Hardware ID: " + hardwareResponse.Value.ToString());
                        STATUS_PCM_PROPS += "|" + hardwareResponse.Value.ToString();
                    }
                    else
                    {
                        this.AddUserMessage("Hardware ID query failed: " + hardwareResponse.Status.ToString());
                        STATUS_PCM_PROPS += "|FAIL";
                    }
                }

                // Disable Serial Number lookup for those that do not provide it
                if (pcmInfo != null && pcmInfo.HardwareType != PcmType.BlackBox)
                {
                    var serialResponse = await this.Vehicle.QuerySerial();

                    if (serialResponse.Status == ResponseStatus.Success)
                    {
                        this.AddUserMessage("Serial Number: " + serialResponse.Value.ToString());
                        STATUS_PCM_PROPS += "|" + serialResponse.Value.ToString();

                    }
                    else
                    {
                        this.AddUserMessage("Serial Number query failed: " + serialResponse.Status.ToString());
                        STATUS_PCM_PROPS += "|FAIL";
                    }
                }

                // Disable BCC lookup for those that do not provide it
                if (pcmInfo != null && pcmInfo.HardwareType != PcmType.P04 && pcmInfo.HardwareType != PcmType.P04_Early && pcmInfo.HardwareType != PcmType.P08)
                {
                    var bccResponse = await this.Vehicle.QueryBCC();
                    if (bccResponse.Status == ResponseStatus.Success)
                    {
                        this.AddUserMessage("Broad Cast Code: " + bccResponse.Value.ToString());
                        STATUS_PCM_PROPS += "|" + bccResponse.Value.ToString();
                    }
                    else
                    {
                        this.AddUserMessage("BCC query failed: " + bccResponse.Status.ToString());
                        STATUS_PCM_PROPS += "|FAIL";
                    }
                }

                var mecResponse = await this.Vehicle.QueryMEC();
                if (mecResponse.Status == ResponseStatus.Success)
                {
                    this.AddUserMessage("MEC: " + mecResponse.Value.ToString());
                    STATUS_PCM_PROPS += "|" + mecResponse.Value.ToString();
                }
                else
                {
                    this.AddUserMessage("MEC query failed: " + mecResponse.Status.ToString());
                    STATUS_PCM_PROPS += "|FAIL";
                }

                var voltageResponse = await this.Vehicle.QueryVoltage();
                if (voltageResponse.Status == ResponseStatus.Success)
                {
                    this.AddUserMessage("Voltage: " + voltageResponse.Value.ToString());
                    STATUS_PCM_PROPS += "|" + voltageResponse.Value.ToString();
                }
                else
                {
                    this.AddUserMessage("Voltage query failed: " + voltageResponse.Status.ToString());
                    STATUS_PCM_PROPS += "|FAIL";
                }

                STATUS_PCM_PROPS += "DONE";
                return;
            }
            catch (Exception exception)
            {
                this.AddUserMessage(exception.Message);
                this.AddDebugMessage(exception.ToString());
            }
            finally
            {
                //this.EnableUserInput();
            }
            STATUS_PCM_PROPS += "DONE";
            return;
        }

        private async Task<string> command_Write_EntireAsync(string payload, StreamWriter writer)
        {
            if (BackgroundWorker.IsAlive)
            {
                AddUserMessage("Error: Cannot write entire, Worker already busy with a task.");
                return "error_alreadybusy";
            }

            string path_data = payload;
            string typeData = WriteType.None.ToString().ToLower();
            AddUserMessage("Attempting write... Payload: " + payload);

            if (payload.Contains(";"))
            {
                var splload = payload.Split(';');
                if (splload.Length > 1)
                {
                    path_data = splload[0];
                    typeData = splload[1].ToLower();
                }
            }

            bool flag = false;
            string cleanPL = path_data; //no filters
            if (cleanPL.StartsWith("path="))
            {
                string path = cleanPL.Replace("path=", "");
                PATH_SAVE_PCM_IMAGE = path;
                AddUserMessage("Writing to PCM from file path: " + path);
                flag = true;
            }
            WriteType wtype = WriteType.None;
            if (flag)
            {
                await ResetDevice();

                //attempt write the loaded file
                var strres = "Performing a write...";
                switch (typeData)
                {
                    case "ospluscalibrationplusboot":
                        strres = "Executing write os + calibration + boot...";
                        wtype = WriteType.OsPlusCalibrationPlusBoot;
                        break;
                    case "calibration":
                        strres = "Executing write calibration...";
                        wtype = WriteType.Calibration;
                        break;
                    case "parameters":
                        strres = "Executing write parameters...";
                        wtype = WriteType.Parameters;
                        break;
                    case "full":
                        strres = "Executing write entire...";
                        wtype = WriteType.Full;
                        break;
                }
                AddUserMessage(strres);


                if (!BackgroundWorker.IsAlive)
                {
                    //if (ConfirmBeforeWrite("This will replace the contents of the flash memory on your PCM."))
                    //wont use a confirm for now, risky but hey its a console app and the user should know what theyre doing
                    {
                        BackgroundWorker = new Thread(() => this.write_BackgroundThread(wtype));
                        BackgroundWorker.IsBackground = true;
                        BackgroundWorker.Start();
                    }
                }
            }

            if (BackgroundWorker.IsAlive)
            {
                //block until background worker is finished, send updates every X seconds through pipe
                Stopwatch timer = new Stopwatch();
                if (writer != null) timer.Start();

                while (true)
                {
                    if (writer != null)
                    {
                        if (timer.Elapsed.Seconds >= 2) //send update every 2 seconds
                        {
                            timer.Stop();
                            timer.Start();

                            //TODO: add documentation to wiki for the message structures
                            var mesg = "status_write_entire|";
                            mesg += STATUS_ACTIVITY + "|";
                            mesg += STATUS_PERCENT_DONE + "|";
                            mesg += STATUS_RETRIES + "|";
                            mesg += STATUS_TIME_REMAINING + "|";
                            mesg += STATUS_TRANSFER_SPEED;
                            SendPipeMessage(writer, mesg);
                        }
                    }
                    else
                    {
                        //writer might be null when sending pipe commands locally in console instead of through a pipe
                        break;
                    }

                    if (!BackgroundWorker.IsAlive)
                    {
                        //return final completion message
                        return "status_write_entire|completed_success";
                    }
                }
            }

            return "status_write_entire|completed_fail";
        }

        private async Task<string> command_Read_EntireAsync(string payload, StreamWriter writer)
        {
            if (BackgroundWorker.IsAlive)
            {
                AddUserMessage("Error: Cannot read entire, Worker already busy with a task.");
                return "error_alreadybusy";
            }


            bool flag = false;
            string cleanPL = payload; //no filters
            if (cleanPL.StartsWith("path="))
            {
                string path = cleanPL.Replace("path=", "");
                PATH_SAVE_PCM_IMAGE = path;
                AddUserMessage("Setting PCM Image Save File Path: " + path);
                flag = true;
            }

            if (flag)
            {
                await ResetDevice();
                //attempt load read file
                AddUserMessage("Executing read entire...");
                if (!BackgroundWorker.IsAlive)
                {
                    BackgroundWorker = new Thread(() => this.readFullContents_BackgroundThread());
                    BackgroundWorker.IsBackground = true;
                    BackgroundWorker.Start();
                }
            }

            if (BackgroundWorker.IsAlive)
            {
                //block until background worker is finished, send updates every X seconds through pipe
                Stopwatch timer = new Stopwatch();
                if (writer != null) timer.Start();

                while (true)
                {
                    if (writer != null)
                    {
                        if (timer.Elapsed.Seconds >= 2) //send update every 2 seconds
                        {
                            timer.Stop();
                            timer.Start();

                            //TODO: add documentation to wiki for the message structures
                            var mesg = "status_read_entire|";
                            mesg += STATUS_ACTIVITY + "|";
                            mesg += STATUS_PERCENT_DONE + "|";
                            mesg += STATUS_RETRIES + "|";
                            mesg += STATUS_TIME_REMAINING + "|";
                            mesg += STATUS_TRANSFER_SPEED;
                            SendPipeMessage(writer, mesg);
                        }
                    }
                    else
                    {
                        //writer might be null when sending pipe commands locally in console instead of through a pipe
                        break;
                    }

                    if (!BackgroundWorker.IsAlive)
                    {
                        //return final completion message
                        return "status_read_entire|completed_success";
                    }
                }
            }

            return "status_read_entire|completed_fail";
        }

        public void SendPipeMessage(StreamWriter writer, string message)
        {
            writer.WriteLine(message);
            writer.Flush();
        }

        public void RegisterPipeCommand(string command, string desc, Func<string, StreamWriter, Task<string>> method)
        {
            pipe_commands.Add(command, new CMD_PipeCommand(command, desc, method));
        }

        public async Task<string> ProcessPipeMessage(StreamWriter server, string message)
        {
            string[] payload = new string[] { message };
            string payload_data = "";
            if (message.Contains("|"))
            {
                payload = message.Split('|');
                if (payload.Length > 1)
                {
                    payload_data = payload[1];
                }
            }

            string command = payload[0].ToLower();

            if (pipe_commands.TryGetValue(command, out CMD_PipeCommand pipeCommand))
            {
                AddUserMessage("Client Pipe Executing Command: " + command);

                Func<string, StreamWriter, Task<string>> method = pipeCommand.funcMethod;
                return await method(payload_data, server);
            }

            return null;
        }


        /// <summary>
        /// Main program initiliazation, welcome message, sets status, processes program arguments and resets j2534 device
        /// </summary>
        public void MainNoForm_Init()
        {
            AddUserMessage(GetAppNameAndVersion());

            try
            {
                StatusUpdateReset();
                ProcessCommandLine();

                //await ResetDevice();
            }
            catch (Exception exception)
            {
                AddUserMessage(exception.Message);
                AddDebugMessage(exception.ToString());
            }
        }

        protected override void ProcessCommandLine()
        {
            bool success = false;
            string[] args = GetCommandArgs();

            Parser.Default.ParseArguments<CommandLineOptionsExtra>(args)
                .WithParsed<CommandLineOptionsExtra>(o =>
                {
                    HIDE_CONSOLE_WINDOW = o.HideConsole;

                    //DeviceConfiguration.Settings.Enable4xReadWrite = picker.Enable4xReadWrite;

                    if (o.ShowCMDVersion)
                    {
                        Console.WriteLine(CMD_BUILD_VERSION);
                        success = true;
                    }

                    if (o.deviceCat == "")
                    {
                        AddUserMessage("Current device category is:");
                        AddUserMessage((DEVICE_CAT == null || DEVICE_CAT == "") ? "NONE" : DEVICE_CAT);

                        success = true;
                    }
                    else if (o.deviceCat != null)
                    {
                        DEVICE_CAT = o.deviceCat;
                        DeviceConfiguration.Settings.DeviceCategory = DEVICE_CAT;
                        DeviceConfiguration.Settings.Save();
                        AddUserMessage("Set device category to " + DEVICE_CAT);
                        success = true;
                    }


                    if (o.deviceType == "")
                    {
                        AddUserMessage("Current device type is:");
                        AddUserMessage((DEVICE_TYPE == null || DEVICE_TYPE == "") ? "NONE" : DEVICE_TYPE);
                        success = true;
                    }
                    else if (o.deviceType != null)
                    {
                        DEVICE_TYPE = o.deviceType;
                        DeviceConfiguration.Settings.J2534DeviceType = DEVICE_TYPE;
                        DeviceConfiguration.Settings.SerialPortDeviceType = DEVICE_TYPE;
                        DeviceConfiguration.Settings.Save();
                        AddUserMessage("Set device type to " + DEVICE_TYPE);
                        success = true;
                    }


                    if (o.deviceCom == "")
                    {
                        AddUserMessage("Current device COM port is:");
                        AddUserMessage((DEVICE_COM_PORT == null || DEVICE_COM_PORT == "") ? "NONE" : DEVICE_COM_PORT);
                        success = true;
                    }
                    else if (o.deviceCom != null)
                    {
                        DEVICE_COM_PORT = o.deviceCom;
                        DeviceConfiguration.Settings.SerialPort = DEVICE_COM_PORT;
                        DeviceConfiguration.Settings.Save();
                        AddUserMessage("Set COM port to " + DEVICE_COM_PORT);
                        success = true;
                    }


                    if (o.devicesList)
                    {
                        ListDevices();
                        success = true;
                    }


                });

            base.ProcessCommandLine();

            if (!success)
                LAST_CONSOLE_INPUT = null;
        }

        private void ListDevices()
        {
            AddLineBreakMessage("Device Category:", ConsoleColor.DarkBlue, ConsoleColor.White);

            int i = 0;
            foreach (var constant in typeof(DeviceConfiguration.Constants).GetFields())
            {
                if (constant.IsLiteral && !constant.IsInitOnly)
                {
                    var msg = ((string)constant.GetValue(null));
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    AddUserMessage("(" + i + ") " + msg);
                    i++;
                }
            }

            AddLineBreakMessage("Serial Device Types:", ConsoleColor.DarkBlue, ConsoleColor.White);
            int c = 0;
            foreach (string dtyp in DeviceTypeList)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                AddUserMessage("(" + c + ") " + dtyp);
                c++;
            }

            AddLineBreakMessage("J2534 Device Types:", ConsoleColor.DarkBlue, ConsoleColor.White);

            int a = 0;
            foreach (J2534DotNet.J2534Device device in J2534DeviceFinder.FindInstalledJ2534DLLs(this))
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                AddUserMessage("(" + a + ") " + device.Name);
                a++;
            }

            if (a == 0) AddUserMessage("No J2534 devices found.");


            AddLineBreakMessage("COM Ports:", ConsoleColor.DarkBlue, ConsoleColor.White);

            int b = 0;
            foreach (object portInfo in PortDiscovery.GetPorts(this))
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                AddUserMessage("(" + b + ") " + portInfo.ToString());
                b++;
            }
            if (b == 0) AddUserMessage("No COM Ports found.");

            AddUserMessage("  ", false);
        }

        private void AddLineBreakMessage(string message, ConsoleColor colorBG, ConsoleColor colorFG)
        {
            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.Black;
            string lines = "";
            for (int i = 0; i < message.Length; i++) lines += "-";
            AddUserMessage(lines, false);
            Console.BackgroundColor = colorBG;
            Console.ForegroundColor = colorFG;
            AddUserMessage(lines, false);
            Console.BackgroundColor = colorBG;
            Console.ForegroundColor = colorFG;
            AddUserMessage(message, false);
            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.White;

        }

        private string[] GetCommandArgs()
        {
            if (LAST_CONSOLE_INPUT != null)
            {
                var rem = LAST_CONSOLE_INPUT.Split(' ');
                var args = LAST_CONSOLE_INPUT.Replace(rem[0] + " ", rem[0] + "|");
                return args.Split('|');
            }
            else
            {
                return Environment.GetCommandLineArgs();
            }
        }

        protected override async Task<bool> ResetDevice()
        {
            if (this.vehicle != null)
            {
                this.vehicle.Dispose();
                this.vehicle = null;
            }
            AddUserMessage("Attempting to connect device... " + DEVICE_CAT + " -> " + DEVICE_TYPE);

            Device device = DeviceFactory.CreateDeviceFromConfigurationSettings(this);

            if (device == null)
            {
                if (DEVICE_CAT == "Serial")
                    device = DeviceFactory.CreateSerialDevice(DEVICE_COM_PORT, DEVICE_TYPE, this);

                if (DEVICE_CAT == "J2534")
                    device = DeviceFactory.CreateJ2534Device(DEVICE_TYPE, this);
            }

            if (device == null)
            {
                LogMessage_YoSelectADifferentDevice();

                //DisableUserInput(); 
                //TODO make a public flag to stop incoming commands potentially interrupting functions
                //(example, a bad message causing this program to hang or crash
                //while in the middle of a write or something similar of that nature)
                //am i paranoid? yes, have i fully studied this source code?
                //no, which is why im probably paranoid, im more of a code first and struggle / research later
                //is it 3 am and i need to get some rest? probably yes
                //will i? not until i cant keep my eyes open anymore
                //if youre reading this send help, and by help i mean another can of monster energy
                //i could've probably coded a function instead of writing this nonsense
                return false;
            }

            AddUserMessage("Device connecting, please wait...");

            Protocol protocol = new Protocol();
            this.vehicle = new Vehicle(
                device,
                protocol,
                this,
                new ToolPresentNotifier(device, protocol, this));

            if (!await this.InitializeCurrentDevice())
            {
                this.vehicle = null;
                return false;
            }

            return true;
        }

        protected override async Task<bool> InitializeCurrentDevice()
        {
            if (this.vehicle == null)
            {
                return false;
            }

            //DisableUserInput();
            //ResetLogs();

            this.AddUserMessage(GetAppNameAndVersion());
            this.AddUserMessage(DateTime.Now.ToString("dddd, MMMM dd yyyy @hh:mm:ss:ff"));

            try
            {
                // TODO: this should not return a boolean, it should just throw 
                // an exception if it is not able to initialize the device.
                Task<bool> initializationTask = this.vehicle.ResetConnection();
                bool completed = await initializationTask.AwaitWithTimeout(TimeSpan.FromSeconds(5));
                if (!completed)
                {
                    throw new TimeoutException("Vehicle.ResetConnection timed out.");
                }

                if (!initializationTask.Result)
                {
                    this.AddUserMessage("Unable to initialize " + this.vehicle.DeviceDescription);

                    LogMessage_YoSelectADifferentDevice();
                    return false;
                }
            }
            catch (Exception exception)
            {
                this.AddUserMessage("Unable to initialize " + this.vehicle.DeviceDescription);

                this.AddDebugMessage(exception.ToString());
                LogMessage_YoSelectADifferentDevice();
                return false;
            }

            if (!this.vehicle.Supports4X)
            {
                DeviceConfiguration.Settings.Enable4xReadWrite = true;
                DeviceConfiguration.Settings.Save();
            }
            this.vehicle.Enable4xReadWrite = DeviceConfiguration.Settings.Enable4xReadWrite;

            await this.ValidDeviceSelectedAsync(this.vehicle.DeviceDescription);

            //TODO: Enable user input again, aka that flag i mentioned in another comment or somethin like that, i dunno.
            //this.EnableUserInput();
            return true;
        }

        protected override Task ValidDeviceSelectedAsync(string deviceName)
        {
            CURRENT_VALID_DEVICE = deviceName;

            return Task.CompletedTask;
        }


        public void LogMessage_YoSelectADifferentDevice()
        {
            AddUserMessage("No device selected.");
            AddUserMessage("Select another device.");
        }


        public override void StatusUpdateActivity(string activity)
        {
            //TODO: STATUS CALLBACKS MAYBE SEND TO CLIENT
            STATUS_ACTIVITY = activity;
        }

        public override void StatusUpdateTimeRemaining(string remaining)
        {
            //TODO: STATUS CALLBACKS MAYBE SEND TO CLIENT
            STATUS_TIME_REMAINING = remaining;
        }

        public override void StatusUpdatePercentDone(string percent)
        {
            //TODO: STATUS CALLBACKS MAYBE SEND TO CLIENT
            STATUS_PERCENT_DONE = percent;
        }

        public override void StatusUpdateRetryCount(string retries)
        {
            //TODO: STATUS CALLBACKS MAYBE SEND TO CLIENT
            STATUS_RETRIES = retries;
        }

        public override void StatusUpdateProgressBar(double completed, bool visible)
        {
            //TODO: STATUS CALLBACKS MAYBE SEND TO CLIENT
            string kpbs = "(" + STATUS_TRANSFER_SPEED + ")";
            string remtime = "[ETA " + STATUS_TIME_REMAINING + "]";
            string ret = (STATUS_RETRIES == string.Empty) ? "" : "(attempt " + STATUS_RETRIES.Split(' ')[0] + ")";
            string fin = STATUS_ACTIVITY + ret + " " + kpbs + " " + remtime;
            UpdateTitleProgressBar(fin, (int)(completed));

            if (!visible)
            {
                UpdateTitleProgressBar("READY", 100);

            }
            //visible // is update progress bar visible
            //(int)(completed * 100); //if so, this is the progress
        }

        public override void StatusUpdateKbps(string Kbps)
        {
            //TODO: STATUS CALLBACKS MAYBE SEND TO CLIENT
            STATUS_TRANSFER_SPEED = Kbps;
        }

        /// <summary>
        /// Write changes to the PCM's flash memory.
        /// </summary>
        protected override async void write_BackgroundThread(WriteType writeType, string path = null)
        {
            using (new AwayMode())
            {
                try
                {
                    this.currentWriteType = writeType;

                    if (this.Vehicle == null)
                    {
                        // This shouldn't be possible - it would mean the buttons 
                        // were enabled when they shouldn't be.
                        return;
                    }

                    this.cancellationTokenSource = new CancellationTokenSource();

                    /*
                    this.Invoke((MethodInvoker)delegate ()
                    {
                        this.DisableUserInput();
                        //this.cancelButton.Enabled = true;

                        if (string.IsNullOrWhiteSpace(path))
                        {
                            path = this.ShowOpenDialog();
                        }
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            return;
                        }

                        DelayDialogBox dialogBox = new DelayDialogBox();
                        DialogResult dialogResult = dialogBox.ShowDialog(this);
                        if (dialogResult == DialogResult.Cancel)
                        {
                            path = null;
                            return;
                        }
                    });
                    */

                    if (path == null)
                    {
                        this.AddUserMessage(
                            writeType == WriteType.TestWrite ?
                                "Test write canceled." :
                                "Write canceled.");
                        return;
                    }

                    this.AddUserMessage(path);

                    byte[] image;
                    using (Stream stream = File.OpenRead(path))
                    {
                        image = new byte[stream.Length];
                        int bytesRead = await stream.ReadAsync(image, 0, (int)stream.Length);
                        if (bytesRead != stream.Length)
                        {
                            // If this happens too much, we should try looping rather than reading the whole file in one shot.
                            this.AddUserMessage("Unable to load file.");
                            return;
                        }
                    }

                    // Sanity checks. 
                    FileValidator validator = new FileValidator(image, this);
                    if (!validator.IsValid())
                    {
                        this.AddUserMessage("This file is corrupt or its format is unknown to PCMHammer. It would render your PCM unusable.");
                        return;
                    }

                    UInt32 kernelVersion = 0;
                    bool needUnlock;
                    int keyAlgorithm = 1;
                    bool shouldHalt;
                    OSIDInfo pcmInfo = null;
                    bool needToCheckOperatingSystem =
                        (writeType != WriteType.OsPlusCalibrationPlusBoot) &&
                        (writeType != WriteType.Full) &&
                        (writeType != WriteType.TestWrite);

                    this.AddUserMessage("Requesting operating system ID...");
                    Response<uint> osidResponse = await this.Vehicle.QueryOperatingSystemId(this.cancellationTokenSource.Token);
                    if (osidResponse.Status == ResponseStatus.Success)
                    {
                        pcmInfo = new OSIDInfo(osidResponse.Value);
                        keyAlgorithm = pcmInfo.KeyAlgorithm;
                        needUnlock = true;

                        if (!validator.IsSameHardware(osidResponse.Value))
                        {
                            return;
                        }

                        if (!validator.IsSameOperatingSystem(osidResponse.Value))
                        {
                            Utility.ReportOperatingSystems(validator.GetOsidFromImage(), osidResponse.Value, writeType, this, out shouldHalt);
                            if (shouldHalt)
                            {
                                return;
                            }
                        }

                        needToCheckOperatingSystem = false;
                    }
                    else
                    {
                        if (this.cancellationTokenSource.Token.IsCancellationRequested)
                        {
                            return;
                        }

                        this.AddUserMessage("Operating system request failed, checking for a live kernel...");

                        kernelVersion = await this.Vehicle.GetKernelVersion();
                        if (kernelVersion == 0)
                        {
                            this.AddUserMessage("Checking for recovery mode...");
                            bool recoveryMode = await this.Vehicle.IsInRecoveryMode();

                            if (recoveryMode)
                            {
                                this.AddUserMessage("PCM is in recovery mode.");
                                needUnlock = true;
                            }
                            else
                            {
                                this.AddUserMessage("PCM is not responding to OSID, kernel version, or recovery mode checks.");
                                this.AddUserMessage("Unlock may not work, but we'll try...");
                                needUnlock = true;
                            }
                            pcmInfo = new OSIDInfo(validator.GetOsidFromImage()); // Prevent Null Reference Exceptions from breaking Recovery Mode
                        }
                        else
                        {
                            needUnlock = false;

                            this.AddUserMessage("Kernel version: " + kernelVersion.ToString("X8"));

                            this.AddUserMessage("Asking kernel for the PCM's operating system ID...");

                            if (needToCheckOperatingSystem)
                            {
                                osidResponse = await this.Vehicle.QueryOperatingSystemIdFromKernel(this.cancellationTokenSource.Token);
                                if (osidResponse.Status != ResponseStatus.Success)
                                {
                                    // The kernel seems broken. This shouldn't happen, but if it does, halt.
                                    this.AddUserMessage("The kernel did not respond to operating system ID query.");
                                    return;
                                }

                                Utility.ReportOperatingSystems(validator.GetOsidFromImage(), osidResponse.Value, writeType, this, out shouldHalt);
                                if (shouldHalt)
                                {
                                    return;
                                }

                                pcmInfo = new OSIDInfo(osidResponse.Value);
                            }

                            needToCheckOperatingSystem = false;
                        }
                    }

                    // Pre flight checks to block invalid write operations by PCM type.
                    if (!pcmInfo.IsSupported)
                    {
                        string msg = $"Abort: The connected {pcmInfo.HardwareType.ToString()} PCM is not supported.";
                        this.AddUserMessage(msg);
                        DialogResult dialogResult = MessageBox.Show(msg, "Abort");
                        return;
                    }

                    if (!pcmInfo.IsSupportedWrite)
                    {
                        string msg = $"Abort: The connected {pcmInfo.HardwareType.ToString()} PCM is not supported for write operations.";
                        this.AddUserMessage(msg);
                        DialogResult dialogResult = MessageBox.Show(msg, "Abort");
                        return;
                    }

                    // If the factory binary is not paritioned we cant write by segment, block the non-full write types
                    if (!pcmInfo.IsSupportedWriteBySegment && (writeType == WriteType.Calibration || writeType == WriteType.OsPlusCalibrationPlusBoot || writeType == WriteType.Parameters))
                    {
                        string msg = $"Error: The connected {pcmInfo.HardwareType.ToString()} PCM binary format is not partitioned and does not support partial write." + Environment.NewLine +
                                    "You will need to do a Write Full Flash (Clone) instead.";
                        this.AddUserMessage(msg);
                        DialogResult dialogResult = MessageBox.Show(msg, "Error");
                        return;
                    }

                    // If we cant write the slave, warn the user of operating system changes
                    if (pcmInfo.HardwareSlaveCPU == true && !pcmInfo.IsSupportedWriteSlaveCPU && (writeType == WriteType.Full || writeType == WriteType.OsPlusCalibrationPlusBoot))
                    {
                        string msg = $"Warning: Writes to the {pcmInfo.HardwareType.ToString()} slave CPU are not supported." + Environment.NewLine +
                                    "You must have another way to update the slave CPU to match when you change operating system, else electronic throttle may not work." + Environment.NewLine +
                                    "Restore this PCM to its original operating system if this happens.";
                        this.AddUserMessage(msg);
                        DialogResult dialogResult = MessageBox.Show(msg, "Warning!", MessageBoxButtons.YesNo);
                        if (dialogResult == DialogResult.No)
                        {
                            this.AddUserMessage("User chose not to proceed.");
                            return;
                        }
                        else
                        {
                            this.AddUserMessage("User chose to proceed.");
                        }
                    }

                    await this.Vehicle.SuppressChatter();

                    if (needUnlock)
                    {

                        bool unlocked = await this.Vehicle.UnlockEcu(keyAlgorithm);
                        if (!unlocked)
                        {
                            this.AddUserMessage("Unlock was not successful.");
                            return;
                        }

                        this.AddUserMessage("Unlock succeeded.");
                    }

                    DateTime start = DateTime.Now;

                    CKernelWriter writer = new CKernelWriter(
                        this.Vehicle,
                        pcmInfo,
                        new Protocol(),
                        writeType,
                        this);

                    await writer.Write(
                        image,
                        kernelVersion,
                        validator,
                        needToCheckOperatingSystem,
                        this.cancellationTokenSource.Token);

                    this.AddUserMessage("Elapsed time " + DateTime.Now.Subtract(start));

                    // This will suppress the scary warnings prior to writing.
                    Configuration.Settings.ConnectionVerified = true;
                }
                catch (IOException exception)
                {
                    this.AddUserMessage(exception.ToString());
                }
                finally
                {
                    this.currentWriteType = WriteType.None;
                    /*
                    this.Invoke((MethodInvoker)delegate ()
                    {
                        this.EnableUserInput();
                        //this.cancelButton.Enabled = false;
                    });
                    */

                    // The token / token-source can only be cancelled once, so we need to make sure they won't be re-used.
                    this.cancellationTokenSource = null;
                }
            }
        }

        /// <summary>
        /// Read the entire contents of the flash.
        /// </summary>
        protected override async void readFullContents_BackgroundThread()
        {
            using (new AwayMode())
            {
                try
                {
                    /*
                    this.Invoke((MethodInvoker)delegate ()
                    {
                        this.DisableUserInput();
                        this.cancelButton.Enabled = true;
                    });
                    */

                    if (this.Vehicle == null)
                    {
                        // This shouldn't be possible - it would mean the buttons 
                        // were enabled when they shouldn't be.
                        this.AddUserMessage("Abort: Vehicle IS NULL");

                        return;
                    }

                    string path = PATH_SAVE_PCM_IMAGE;

                    if (path == null)
                    {
                        this.AddUserMessage("Read canceled.");
                        return;
                    }

                    if (path != "STREAM")
                        this.AddUserMessage("Will try to save to " + path);

                    this.cancellationTokenSource = new CancellationTokenSource();

                    this.AddUserMessage("Querying operating system of current PCM.");

                    Response<uint> osidResponse = await this.Vehicle.QueryOperatingSystemId(this.cancellationTokenSource.Token);
                    if (osidResponse.Status != ResponseStatus.Success)
                    {
                        this.AddUserMessage("Operating system query failed, will retry: " + osidResponse.Status);
                        await this.Vehicle.ExitKernel();

                        osidResponse = await this.Vehicle.QueryOperatingSystemId(this.cancellationTokenSource.Token);
                        if (osidResponse.Status != ResponseStatus.Success)
                        {
                            this.AddUserMessage("Operating system query failed: " + osidResponse.Status);
                        }
                    }

                    OSIDInfo pcmInfo;
                    if (osidResponse.Status == ResponseStatus.Success)
                    {
                        // Look up the information about this PCM, based on the OSID;
                        this.AddUserMessage("OSID: " + osidResponse.Value);
                        pcmInfo = new OSIDInfo(osidResponse.Value);
                        this.AddUserMessage("Description: " + pcmInfo.Description);
                    }
                    else
                    {
                        this.AddUserMessage("Unable to get operating system ID. Will assume this can be unlocked with the default seed/key algorithm.");

                        UInt32 OperatingSystemId = FALLBACK_OPERATING_SYSTEM_ID;

                        if (OperatingSystemId == 0)
                        {
                            this.AddUserMessage("Abort: no fallback Operating System ID provided.");
                            return;
                        }
                        await Vehicle.ForceSendToolPresentNotification();

                        pcmInfo = new OSIDInfo(OperatingSystemId); // osid

                        AddUserMessage($"Using OsID: {pcmInfo.OSID}");
                    }

                    // Pre flight checks to block invalid write operations by PCM type.
                    if (!pcmInfo.IsSupported)
                    {
                        string msg = $"Abort: The connected {pcmInfo.HardwareType.ToString()} PCM is not supported.";
                        this.AddUserMessage(msg);
                        return;
                    }

                    if (!pcmInfo.IsSupportedRead)
                    {
                        string msg = $"Abort: The connected {pcmInfo.HardwareType.ToString()} PCM is not supported for read operations.";
                        this.AddUserMessage(msg);
                        return;
                    }

                    if (pcmInfo.HardwareType == PcmType.P05)
                    {
                        string msg = $"WARNING: {pcmInfo.HardwareType.ToString()} Support is still in development.";
                        this.AddUserMessage(msg);

                        if (!BYPASS_INDEV_PCM_SUPPORT)
                        {
                            this.AddUserMessage("User chose not to proceed.");
                            return;
                        }
                    }

                    await this.Vehicle.SuppressChatter();

                    bool unlocked = await this.Vehicle.UnlockEcu(pcmInfo.KeyAlgorithm);
                    if (!unlocked)
                    {
                        this.AddUserMessage("Unlock was not successful.");
                        return;
                    }

                    this.AddUserMessage("Unlock succeeded.");

                    if (cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        return;
                    }

                    // Do the actual reading.
                    DateTime start = DateTime.Now;

                    CKernelReader reader = new CKernelReader(
                        this.Vehicle,
                        pcmInfo,
                        this);

                    Response<Stream> readResponse = await reader.ReadContents(cancellationTokenSource.Token);

                    this.AddUserMessage("Elapsed time " + DateTime.Now.Subtract(start));
                    if (readResponse.Status != ResponseStatus.Success)
                    {
                        this.AddUserMessage("Read failed, " + readResponse.Status.ToString());
                        return;
                    }

                    // This will suppress the scary warnings prior to writing.
                    PcmHacking.Configuration.Settings.ConnectionVerified = true;

                    // Save the contents to the path that the user provided.
                    bool success = false;
                    do
                    {
                        try
                        {
                            bool saveToPath = path != "STREAM";

                            if (saveToPath)
                                this.AddUserMessage("Saving contents to " + path);

                            readResponse.Value.Position = 0;

                            if (saveToPath)
                            {
                                using (Stream output = File.Open(path, FileMode.Create))
                                {
                                    await readResponse.Value.CopyToAsync(output);
                                }
                            }
                            else
                            {
                                read_memoryStream = new MemoryStream();
                                using (read_memoryStream)
                                {
                                    await readResponse.Value.CopyToAsync(read_memoryStream);
                                }
                            }

                            success = true;
                        }
                        catch (IOException exception)
                        {
                            this.AddUserMessage("Unable to save / stream pcm file: " + exception.Message);
                            this.AddDebugMessage(exception.ToString());
                        }
                    } while (!success);
                }
                catch (Exception exception)
                {
                    this.AddUserMessage("Read failed: " + exception.ToString());
                }
                finally
                {
                    // The token / token-source can only be cancelled once, so we need to make sure they won't be re-used.
                    this.cancellationTokenSource = null;
                }
            }
        }

        /// <summary>
        /// Add user message to console window
        /// </summary>
        public override void AddUserMessage(string message)
        {
            //UpdateTitleProgressBar("Testing", (int)((double)DateTime.Now.Second * 1.65d));

            string timestamp = DateTime.Now.ToString("hh:mm:ss:fff");
            var bgCol = Console.BackgroundColor;
            var fgCol = Console.ForegroundColor;

            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.White;

            Console.Write("[" + timestamp + "]  ");

            Console.BackgroundColor = bgCol;
            Console.ForegroundColor = fgCol;

            Console.Write(message);
            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(Environment.NewLine);

        }

        /// <summary>
        /// Add user message to console window
        /// </summary>
        public void AddUserMessage(string message, bool showTimeStamp)
        {
            string timestamp = DateTime.Now.ToString("hh:mm:ss:fff");
            var bgCol = Console.BackgroundColor;
            var fgCol = Console.ForegroundColor;

            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = showTimeStamp ? ConsoleColor.White : ConsoleColor.Black;

            Console.Write("[" + timestamp + "]  ");

            Console.BackgroundColor = bgCol;
            Console.ForegroundColor = fgCol;

            Console.Write(message);
            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(Environment.NewLine);

        }

        /// <summary>
        /// Add debug message to console window
        /// </summary>
        public override void AddDebugMessage(string message)
        {
            if (!SHOW_DEBUG_MESSAGES)
                return;

            string timestamp = DateTime.Now.ToString("hh:mm:ss:fff");

            var bgCol = Console.BackgroundColor;
            var fgCol = Console.ForegroundColor;

            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.White;

            Console.Write("[" + timestamp + "] [DEBUG] ");

            Console.BackgroundColor = bgCol;
            Console.ForegroundColor = fgCol;

            Console.Write(message);
            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(Environment.NewLine);

        }

    }
}