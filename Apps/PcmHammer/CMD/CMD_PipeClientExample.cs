using System;
using System.IO;
using System.IO.Pipes;

namespace PcmHammer
{
    public class CMD_PipeClientExample
    {
        private string CMD_PCMHAMMER_PIPE = "cmd_pcmhammer_pipe";

        public CMD_PipeClientExample()
        {
            using (var pipeClient = new NamedPipeClientStream(".", CMD_PCMHAMMER_PIPE, PipeDirection.InOut))
            {
                Console.WriteLine("Waiting to connect to server");
                if (!pipeClient.IsConnected) { pipeClient.Connect(); }

                Console.WriteLine("Connected!");
                using (var reader = new StreamReader(pipeClient))
                {
                    using (var writer = new StreamWriter(pipeClient))
                    {
                        //moved up here as it can cause an overflow if readline doesnt block, oops.
                        Console.WriteLine("Waiting for server handshake");
                        var running = true;
                        while (running)
                        {
                            //you wont be able to write/send anything until the server confirms you are connected

                            try
                            {
                                var message = reader.ReadLine();
                                if (message != null)
                                {
                                    Console.WriteLine("Recieved from server {0}", message);
                                    switch (message)
                                    {
                                        //connection confirmed
                                        case "server_handshake":
                                            //you are now connected to the server, send or request data:
                                            //check CMD_MainNoForm.cs for the registered command list or the github wiki.

                                            //non-working example, this wont work because you need to send your connected device data first,
                                            //or request the available device list and send back one the device details
                                            writer.WriteLine("read_entire");
                                            writer.Flush();
                                            break;
                                        case "quit":
                                            running = false;
                                            //server diconnected you for whatever reason, usually an error or problem server side.
                                            break;
                                    }
                                }
                            }
                            catch (IOException e)
                            {
                                Console.WriteLine("IOException: " + e.Message);
                                running = false;
                            }
                            catch (OutOfMemoryException e)
                            {
                                Console.WriteLine("OutOfMemoryException: " + e.Message);
                                running = false;
                            }
                        }
                    }
                }
            }
            Console.WriteLine("Client Quits");
        }
    }
}
