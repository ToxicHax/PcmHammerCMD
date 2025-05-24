using System;
using System.IO;
using System.IO.Pipes;

namespace CMDVersion
{
    public class CMD_PipeServer
    {
        private IPipeCallback pipeCallback;

        public CMD_PipeServer(IPipeCallback pipeCallback)
        {
            this.pipeCallback = pipeCallback;
            NamedPipeServerStream pipeServer = new NamedPipeServerStream("cmd_pcmhammer_pipe", PipeDirection.InOut);

            using (pipeServer)
            {
                Log("CMD Pipe server running..");

                try
                {
                    HandlePipe(pipeServer);
                }
                catch (IOException e) //disconnected or other error
                {
                    Log("error: " + e.Message);
                }
            }
        }

        public void ReturnMessage(string message, StreamWriter writer)
        {
            //Log("Sending data back to client: " + message);

            //any extra processing to the reply data we can do here,
            //for now we just send the data back as its given to us by the commands result.

            writer.WriteLine(message);
            writer.Flush();
        }

        private void HandlePipe(NamedPipeServerStream pipeServer)
        {
            using (var reader = new StreamReader(pipeServer))
            {
                using (var writer = new StreamWriter(pipeServer))
                {
                    var running = true;
                    pipeServer.WaitForConnection();
                    writer.WriteLine("server_handshake");
                    Log("CMD Client connected, sending handshake");
                    writer.Flush();
                    while (running)
                    {
                        try
                        {
                            pipeServer.WaitForPipeDrain();
                            var message = reader.ReadLine();
                            if (message != null)
                            {
                                if (message != "quit")
                                {
                                    //Blocking until we get result, works for now
                                    var reply = pipeCallback.ProcessPipeMessage(writer, message).GetAwaiter().GetResult();
                                    ReturnMessage(reply, writer);
                                }
                                else
                                {
                                    running = false;
                                }
                            }
                        }
                        catch (IOException e)
                        {
                            Log("IOException: " + e.Message);
                            running = false;
                        }
                        catch (OutOfMemoryException e)
                        {
                            Log("OutOfMemoryException: " + e.Message);
                            running = false;
                        }
                    }
                }
            }
        }

        private void Log(string v)
        {
            string timestamp = DateTime.Now.ToString("hh:mm:ss:fff");
            Console.WriteLine("[" + timestamp + "]  " + "[CMDPipe] " + v);
        }
    }
}