using System;
using System.Configuration;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.AccessControl;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using Castle.Core.Logging;
using Castle.Facilities.Logging;
using Castle.MicroKernel.Registration;
using Castle.Windsor;
using EventStore.ClientAPI;
using log4net.Core;
using log4net.Layout.Pattern;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using NServiceKit.Redis;
using PsychNet.CQRS;
using PsychNet.CQRS.CommandHandlers;
using PsychNet.CQRS.Commands;
using PsychNet.CQRS.EventHandlers;
using PsychNet.Notifier;
using PsychNet.SetsterApiClient;
using RestSharp;
using SetsterApiClient;
using Topshelf;

namespace PsychNet.EventService
{
    class Program
    {
        private static IWindsorContainer container;

        static void Main(string[] args)
        {
            container = new WindsorContainer();
            RegisterComponents();

            var runner = container.Resolve<IService>();

            HostFactory.Run(x =>
            {
                x.Service<IService>(s =>                        
                {
                    s.ConstructUsing(name => runner);   
                    s.WhenStarted(tc => tc.Start());
                    s.WhenStopped(tc =>
                    {
                        tc.Stop();
                        container.Release(runner);
                    });
                });
                x.RunAsLocalSystem();

                x.SetDescription("PsychNet.EventService");
                x.SetDisplayName("PsychNet.EventService");
                x.SetServiceName("PsychNet.EventService");                       
            });       
        }

        static void RegisterComponents()
        {
            ConnectionSettings settings = ConnectionSettings.Create();

            var eventStoreIp = ConfigurationManager.AppSettings["eventStoreIp"];
            var eventStorePort = ConfigurationManager.AppSettings["eventStorePort"];
            var endpoint = new IPEndPoint(IPAddress.Parse(eventStoreIp), Convert.ToInt32(eventStorePort));
            var connection = EventStoreConnection.Create(settings, endpoint);
            connection.Connect();

            container.Register(Component.For<IService>().ImplementedBy<PsychNetEventService>().LifeStyle.Transient);

            var commandSender = new CommandSender();
            var eventPublisher = new EventPublisher();

            container.Register(Component.For<IEventStoreConnection>().Instance(connection));
            container.Register(Component.For<INotifier>().ImplementedBy<Notifier.Notifier>().DependsOn(Dependency.OnAppSettingsValue("environment")));
            container.Register(Component.For<ICommandSender>().Instance(commandSender));
            container.Register(Component.For<IEventPublisher>().Instance(eventPublisher));
            container.Register(Component.For<ISMSClient>().ImplementedBy<TwilioClient>().DependsOn(Dependency.OnAppSettingsValue("twilioAuthToken")).DependsOn(Dependency.OnAppSettingsValue("twilioAccountSid")).DependsOn(Dependency.OnAppSettingsValue("environment")).DependsOn(Dependency.OnAppSettingsValue("twilioSenderNumber")));
            container.Register(Component.For<ISetsterClient>().ImplementedBy<SetsterClient>().DependsOn(Dependency.OnAppSettingsValue("setsterAccountAccessToken")).DependsOn(Dependency.OnAppSettingsValue("setsterAccountEmailAddress")));
            container.Register(Component.For<IRestClient>().ImplementedBy<RestClient>());

            container.AddFacility<LoggingFacility>(a => a.UseLog4Net());
            log4net.Config.XmlConfigurator.Configure();

            container.Register(Component.For<IRedisClient>().ImplementedBy<RedisClient>());
            var redisClient = container.Resolve<IRedisClient>();

            var repository = new Repository(connection, redisClient);
            container.Register(Component.For<IRepository>().Instance(repository));

            var adminEventHandler = new AdminEventHandler(repository, commandSender);
            var programmePoolEventHandler = new ProgrammePoolEventHandler(repository, commandSender);
            var fileEventHandler = new FileEventHandler(repository, commandSender);
            var therapistPoolEventHandler = new TherapistPoolEventHandler(repository, commandSender);
            var clientPoolEventHandler = new ClientPoolEventHandler(repository, commandSender);
            var therapistEventHandler = new TherapistEventHandler(repository, commandSender);
            var programmeEventHandler = new ProgrammeEventHandler(repository, commandSender);
            var clientEventHandler = new ClientEventHandler(repository, commandSender);
            var relationshipEventHandler = new RelationshipEventHandler(repository, commandSender);
            var quaireEventHandler = new QuaireEventHandler(repository, commandSender);
            var quairePoolEventHandler = new QuairePoolEventHandler(repository, commandSender);
            var adminPoolEventHandler = new AdminPoolEventHandler(repository, commandSender);

            eventPublisher.RegisterEventHandlers(new IHandleEvents[]
                {
                    adminEventHandler,
                    programmePoolEventHandler, 
                    fileEventHandler, 
                    therapistPoolEventHandler, 
                    clientPoolEventHandler,
                    therapistEventHandler,
                    programmeEventHandler,
                    clientEventHandler,
                    relationshipEventHandler,
                    quaireEventHandler,
                    quairePoolEventHandler,
                    adminPoolEventHandler

                });


            container.Register(Component.For<IClientCommandHandler>().ImplementedBy<ClientCommandHandler>().DependsOn(Dependency.OnAppSettingsValue("environment")));
            container.Register(Component.For<IRelationshipCommandHandler>().ImplementedBy<RelationshipCommandHandler>());
            container.Register(Component.For<IProgrammeCommandHandler>().ImplementedBy<ProgrammeCommandHandler>());
            container.Register(Component.For<ITherapistCommandHandler>().ImplementedBy<TherapistCommandHandler>().DependsOn(Dependency.OnAppSettingsValue("environment")));
            container.Register(Component.For<IProgrammePoolCommandHandler>().ImplementedBy<ProgrammePoolCommandHandler>());
            container.Register(Component.For<ITherapistPoolCommandHandler>().ImplementedBy<TherapistPoolCommandHandler>());
            container.Register(Component.For<IClientPoolCommandHandler>().ImplementedBy<ClientPoolCommandHandler>());
            container.Register(Component.For<IFileCommandHandler>().ImplementedBy<FileCommandHandler>());
            container.Register(Component.For<IAdminCommandHandler>().ImplementedBy<AdminCommandHandler>());
            container.Register(Component.For<IQuaireCommandHandler>().ImplementedBy<QuaireCommandHandler>());
            container.Register(Component.For<IQuairePoolCommandHandler>().ImplementedBy<QuairePoolCommandHandler>());
            container.Register(Component.For<IAdminPoolCommandHandler>().ImplementedBy<AdminPoolCommandHandler>());

            commandSender.RegisterCommandHandlers(container);
        }
    }

    public interface IService
    {
        void Start();
        void Stop();
    }

    public class PsychNetEventService : IService
    {
        readonly ICommandSender _commandSender;
        private Castle.Core.Logging.ILogger logger = NullLogger.Instance;
        ActionableQueue[] _allAqs;
        static Timer _timer;

        public Castle.Core.Logging.ILogger Logger
        {
            get { return logger; }
            set { logger = value; }
        }

        public PsychNetEventService(ICommandSender commandSender)
        {
            _commandSender = commandSender;
        }

        public void Start()
        {

            Console.WriteLine("Starting Service ...");

            var connectionString = ConfigurationManager.AppSettings["cloudStorageConnectionString"];
            var storageAccount = CloudStorageAccount.Parse(connectionString);

            var queueClient = storageAccount.CreateCloudQueueClient();

            var reminderNotificationsQ = queueClient.GetQueueReference("remindernotificationsq");
            var cacheMaintenanceQ = queueClient.GetQueueReference("cachemaintenanceq");

            reminderNotificationsQ.CreateIfNotExists();
            cacheMaintenanceQ.CreateIfNotExists();

            var reminderNotificationAq = new ActionableQueue
            {
                Queue = reminderNotificationsQ,
                Action = () => _commandSender.Send(new SendAllDueReminders())
            };

            var cacheMaintenanceAq = new ActionableQueue
            {
                Queue = cacheMaintenanceQ,
                Action = () => _commandSender.Send(new ClearAllCaches())
            };

            _allAqs = new[] {reminderNotificationAq, cacheMaintenanceAq };

            _timer = new Timer(_ => OnCallBack(), null, 0, 1000*30);
        }

        public void Stop()
        {
            Console.WriteLine("Stopping the service ...");
        }

        public void OnCallBack()
        {
            foreach (var aq in _allAqs)
            {
                aq.Queue.FetchAttributes();

                var count = aq.Queue.ApproximateMessageCount;

                if (count > 0)
                {
                    try
                    {
                        var message = aq.Queue.GetMessage();
                        if (message != null)
                        {
                            Logger.InfoFormat("Got message: {0}", message.AsString);

                            aq.Action();

                            aq.Queue.DeleteMessage(message);
                        }
                    }
                    catch (StorageException e)
                    {
                        Logger.WarnFormat(e.Message);
                    }
                    catch (Exception e)
                    {
                        Logger.WarnFormat(e.Message);
                    }

                }
                else
                {
                    Logger.DebugFormat("No messages found for queue {0}.", aq.Queue.Name);
                }
            }

            Logger.Debug("Waiting...");
        }
    }

    public class ActionableQueue
    {
        public CloudQueue Queue { get; set; }
        public Action Action { get; set; }
    }

    [Serializable]
    public class SendNotificationsAsyncCommand
    {
        public string Message { get; set; }
    }
}
