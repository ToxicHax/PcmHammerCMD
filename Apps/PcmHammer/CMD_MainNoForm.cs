using CommandLine;
using PcmHacking;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

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
        public string LAST_CONSOLE_INPUT = null;

        /// <summary>
        /// set to true to use in development pcms that may not be fully supported yet, e.g. p05 pcm's
        /// </summary>
        public bool BYPASS_INDEV_PCM_SUPPORT = false;

        /// <summary>
        /// if the os id cannot be read from the pcm instead fall back to this os id.
        /// </summary>
        public uint FALLBACK_OPERATING_SYSTEM_ID = 0;

        public bool SHOW_DEBUG_MESSAGES = true;

        public bool HIDE_CONSOLE_WINDOW = false;


        public Dictionary<string, Func<string, string>> pipe_commands = new Dictionary<string, Func<string, string>>();
        private MemoryStream read_memoryStream;

        public CMD_MainNoForm()
        {
            Task<CMD_PipeServer> task = Task.Run(() => new CMD_PipeServer(this));

            RegisterPipeCommand("read_entire", command_Read_Entire);
            RegisterPipeCommand("write_entire", command_Write_Entire);

            MainNoForm_Init();

            if (HIDE_CONSOLE_WINDOW)
            {
                var handle = GetConsoleWindow();
                ShowWindow(handle, CMD_HIDE); //5 = SHOW
            }


            //
            DoConsoleInput();

            AddUserMessage("Server is still running, WARNING: forcefully closing this window could cause damages to any PCM's currently being written to.");

            DoConsoleInput();

            //TODO: REQUEST SERVER THREAD TO EXIT AND SEE REPORT BACK WITH PROGRESS MAYBE INCASE ANY ON GOING WRITES OR READS ARE HAPPENING
            AddUserMessage("Requesting graceful exit from server... please wait. WARNING: forcefully closing this window could cause damages to any PCM's currently being written to.");

            //and wait for on the pipe server for messages to get processed or getting a quit message to finish.
            task.Wait();
        }

        private void DoConsoleInput()
        {
            bool running = true;
            while (running)
            {
                string input = Console.ReadLine().ToLower();
                LAST_CONSOLE_INPUT = input;
                if (LAST_CONSOLE_INPUT.Length > 0)
                {
                    if (input == "quit" || input == "exit")
                    {
                        running = false;
                    }
                    else
                    {
                        string replyData = ProcessPipeMessage(input.Replace(" ", "|"));
                        //TODO: process the reply data if needed, and make a little wrapper for the data to allow multiple data types, not just strings

                        if (replyData == null)
                        {
                            ProcessCommandLine();
                            if (LAST_CONSOLE_INPUT == null)
                            {
                                AddUserMessage("No pipe command or args found for '" + input + "'!");
                            }
                        }
                    }
                }
            }
        }

        private string command_Write_Entire(string payload)
        {
            AddUserMessage("CLIENT REQUESTING: WRITE ENTIRE");
            //TODO: WRITE ENTIRE WITH PROVIDED PAYLOAD, SEND BACK EITHER SUCCESS OR ERROR MESSAGE
            return "empty_reply";
        }

        private string command_Read_Entire(string payload)
        {
            AddUserMessage("CLIENT REQUESTING: READ ENTIRE");
            //TODO: READ ENTIRE, SEND BACK EITHER ERROR MESSAGE OR PCM BIN
            return "empty_reply";
        }

        public void RegisterPipeCommand(string command, Func<string, string> obj)
        {
            pipe_commands.Add(command, obj);
        }

        public string ProcessPipeMessage(string message)
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

            if (pipe_commands.TryGetValue(command, out Func<string, string> method))
            {
                return method(payload_data);
            }

            return null;
        }


        /// <summary>
        /// Main program initiliazation, welcome message, sets status, processes program arguments and resets j2534 device
        /// </summary>
        public async void MainNoForm_Init()
        {
            AddUserMessage(GetAppNameAndVersion());

            try
            {
                StatusUpdateReset();
                ProcessCommandLine();

                await ResetDevice();
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

                    if (o.ShowCMDVersion)
                    {
                        Console.WriteLine(CMD_BUILD_VERSION);
                        success = true;
                    }

                    if (o.deviceCat != null)
                    {
                        DEVICE_CAT = o.deviceCat;
                        success = true;
                    }

                    if (o.deviceType != null)
                    {
                        DEVICE_TYPE = o.deviceType;
                        success = true;
                    }

                    if (o.deviceCom != null)
                    {
                        DEVICE_COM_PORT = o.deviceCom;
                        success = true;
                    }
                });
            base.ProcessCommandLine();

            if (!success)
                LAST_CONSOLE_INPUT = null;
        }

        private string[] GetCommandArgs()
        {
            if (LAST_CONSOLE_INPUT != null)
            {
                return LAST_CONSOLE_INPUT.Split(' ');
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

            Device device = null;

            if (DEVICE_CAT == "0")
                device = DeviceFactory.CreateSerialDevice(DEVICE_COM_PORT, DEVICE_TYPE, this);

            if (DEVICE_CAT == "1")
                device = DeviceFactory.CreateJ2534Device(DEVICE_TYPE, this);

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

        public void LogMessage_YoSelectADifferentDevice()
        {
            AddUserMessage("No device selected.");
            AddUserMessage("Select another device.");
        }


        public override void StatusUpdateActivity(string activity)
        {
            //TODO: STATUS CALLBACKS MAYBE SEND TO CLIENT
        }

        public override void StatusUpdateTimeRemaining(string remaining)
        {
            //TODO: STATUS CALLBACKS MAYBE SEND TO CLIENT
        }

        public override void StatusUpdatePercentDone(string percent)
        {
            //TODO: STATUS CALLBACKS MAYBE SEND TO CLIENT
        }

        public override void StatusUpdateRetryCount(string retries)
        {
            //TODO: STATUS CALLBACKS MAYBE SEND TO CLIENT
        }

        public override void StatusUpdateProgressBar(double completed, bool visible)
        {
            //TODO: STATUS CALLBACKS MAYBE SEND TO CLIENT
            //visible // is update progress bar visible
            //(int)(completed * 100); //if so, this is the progress
        }

        public override void StatusUpdateKbps(string Kbps)
        {
            //TODO: STATUS CALLBACKS MAYBE SEND TO CLIENT
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
                    Configuration.Settings.ConnectionVerified = true;

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
            string timestamp = DateTime.Now.ToString("hh:mm:ss:fff");
            Console.Write("[" + timestamp + "]  " + message + Environment.NewLine);
        }

        /// <summary>
        /// Add debug message to console window
        /// </summary>
        public override void AddDebugMessage(string message)
        {
            if (!SHOW_DEBUG_MESSAGES)
                return;

            string timestamp = DateTime.Now.ToString("hh:mm:ss:fff");
            Console.Write("[" + timestamp + "] [DEBUG]  " + message + Environment.NewLine);
        }

    }
}