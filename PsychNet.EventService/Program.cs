using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Topshelf;

namespace PsychNet.EventService
{
    class Program
    {
        static void Main(string[] args)
        {
            HostFactory.Run(x =>                                 
            {
                x.Service<IService>(s =>                        
                {
                    s.ConstructUsing(name => new PsychNetEventService());   
                    s.WhenStarted(tc => tc.Start());             
                    s.WhenStopped(tc => tc.Stop());              
                });
                x.RunAsLocalSystem();

                x.SetDescription("PsychNet.EventService");
                x.SetDisplayName("PsychNet.EventService");
                x.SetServiceName("PsychNet.EventService");                       
            });       
        }
    }

    public interface IService
    {
        void Start();
        void Stop();
    }

    public class PsychNetEventService : IService
    {
        public void Start()
        {
            Console.WriteLine("Starting Service ...");
        }

        public void Stop()
        {
            Console.WriteLine("Stopping the service ...");
        }
    }
}
