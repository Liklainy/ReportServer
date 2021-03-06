﻿using System;
using System.Configuration;
using System.IO;
using System.Net;
using System.Reflection;
using Autofac;
using AutoMapper;
using Monik.Client;
using Nancy;
using Nancy.Bootstrapper;
using Nancy.Bootstrappers.Autofac;
using ReportService.Core;
using ReportService.Interfaces;
using ReportService.Nancy;
using ReportService.View;
using SevenZip;
using Telegram.Bot;

namespace ReportService
{
    public interface IPrivateBootstrapper
    {
        void PrivateConfigureApplicationContainer(ILifetimeScope existingContainer);
    }

    public partial class Bootstrapper : AutofacNancyBootstrapper
    {
        public static ILifetimeScope Global; //mech of work?

        public ILifetimeScope Container => ApplicationContainer;

        protected override void ApplicationStartup(ILifetimeScope container, IPipelines pipelines)
        {
            Global = Container;
            // No registrations should be performed in here, however you may
            // resolve things that are needed during application startup.
            ILogic log = Container.Resolve<ILogic>();
            log.Start();
        }

        protected override void ConfigureApplicationContainer(ILifetimeScope existingContainer)
        {
            // Perform registration that should have an application lifetime
            existingContainer
                .RegisterNamedImplementation<IDataExecutor, CommonDataExecutor>("commondataex");
            existingContainer
                .RegisterNamedImplementation<IViewExecutor, CommonViewExecutor>("commonviewex");
            existingContainer
                .RegisterNamedImplementation<IViewExecutor, TaskListViewExecutor>("tasklistviewex");
            existingContainer
                .RegisterNamedImplementation<IViewExecutor, InstanceListViewExecutor>("instancelistviewex");
            existingContainer
                .RegisterSingleton<IPostMaster, PostMasterWork>();
            existingContainer
                .RegisterSingleton<ILogic, Logic>();
            existingContainer
                .RegisterImplementation<IRTask, RTask>();

            var repository = new Repository(ConfigurationManager.AppSettings["DBConnStr"]);
            existingContainer
                .RegisterInstance<IRepository, Repository>(repository);

            // Partial bootstrapper for private named implementations registration
            IPrivateBootstrapper privboots = this as IPrivateBootstrapper;
            if (privboots != null)
                privboots
                    .PrivateConfigureApplicationContainer(existingContainer);

            #region ConfigureMonik

            var logSender = new AzureSender(
                ConfigurationManager.AppSettings["monikendpoint"],
                "incoming");

            existingContainer
                .RegisterInstance<IClientSender, AzureSender>(logSender);

            var monikSettings = new ClientSettings()
            {
                SourceName = "ReportServer",
                InstanceName = ConfigurationManager.AppSettings["InstanceName"],
                AutoKeepAliveEnable = true
            };

            existingContainer
                .RegisterInstance<IClientSettings, ClientSettings>(monikSettings);

            existingContainer
                .RegisterSingleton<IClientControl, MonikInstance>();

            #endregion

            #region ConfigureMapper

            var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile(typeof(MapperProfile)));
            // Hint: add to ctor if many profileSs needed: cfg.AddProfile(typeof(AutoMapperProfile));
            existingContainer.RegisterSingleInstance<MapperConfiguration, MapperConfiguration>(mapperConfig);
            var mapper = existingContainer.Resolve<MapperConfiguration>().CreateMapper();
            existingContainer.RegisterSingleInstance<IMapper, IMapper>(mapper);

            #endregion

            #region ConfigureCompressor

            var path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                Environment.Is64BitProcess ? "x64" : "x86", "7z.dll");
            SevenZipBase.SetLibraryPath(path);
            var compressor = new SevenZipCompressor
            {
                CompressionMode = CompressionMode.Create,
                ArchiveFormat = OutArchiveFormat.SevenZip
            };
            var archiver = new Archiver7Zip(compressor);
            existingContainer.RegisterSingleInstance<IArchiver, Archiver7Zip>(archiver);

            #endregion

            #region ConfigureBot

            Uri proxyUri = new Uri(ConfigurationManager.AppSettings["proxyUriAddr"]);
            ICredentials credentials = new NetworkCredential(ConfigurationManager.AppSettings["proxyLogin"],
                ConfigurationManager.AppSettings["proxyPassword"]);
            WebProxy proxy = new WebProxy(proxyUri, true, null, credentials);
            TelegramBotClient bot = new TelegramBotClient(ConfigurationManager.AppSettings["BotToken"], proxy);
            existingContainer
                .RegisterSingleInstance<ITelegramBotClient, TelegramBotClient>(bot);

            #endregion

            existingContainer //why?
                .RegisterInstance<ILifetimeScope, ILifetimeScope>(existingContainer);
        }

        protected override void ConfigureRequestContainer(ILifetimeScope container, NancyContext context)
        {
            // Perform registrations that should have a request lifetime
        }

        protected override void RequestStartup(ILifetimeScope container, IPipelines pipelines, NancyContext context)
        {
            // No registrations should be performed in here, however you may
            // resolve things that are needed during request startup.
        }
    }

    public static class LifeTimeExtension
    {
        public static void RegisterSingleton<TInterface, TImplementation>(this ILifetimeScope container)
        {
            container.Update(builder => builder
                .RegisterType<TImplementation>()
                .As<TInterface>()
                .SingleInstance());
        }

        public static void RegisterImplementation<TInterface, TImplementation>(this ILifetimeScope container)
        {
            container.Update(builder => builder
                .RegisterType<TImplementation>()
                .As<TInterface>());
        }

        public static void RegisterInstance<TInterface, TImplementation>(this ILifetimeScope container,
            TImplementation aInstance)
        {
            container.Update(builder => builder
                .Register(x => aInstance)
                .As<TInterface>());
        }

        public static void RegisterSingleInstance<TInterface, TImplementation>(this ILifetimeScope container,
            TImplementation aInstance)
        {
            container.Update(builder => builder
                .Register(x => aInstance)
                .As<TInterface>()
                .SingleInstance());
        }

        public static void RegisterNamedSingleton<TInterface, TImplementation>(this ILifetimeScope container,
            string name)
        {
            container.Update(builder => builder
                .RegisterType<TImplementation>()
                .Named<TInterface>(name)
                .SingleInstance());
        }

        public static void RegisterNamedImplementation<TInterface, TImplementation>(this ILifetimeScope container,
            string name)
        {
            container.Update(builder => builder
                .RegisterType<TImplementation>()
                .Named<TInterface>(name));
        }
    } //extensions

    public class MapperProfile : Profile
    {
        public MapperProfile()
        {
            CreateMap<DtoRecepientGroup, RRecepientGroup>();
            CreateMap<RTask, ApiFullTask>()
                .ForMember("ScheduleId", opt => opt.MapFrom(s => s.Schedule.Id))
                .ForMember("RecepientGroupId", opt => opt.MapFrom(s => s.SendAddresses.Id))
                .ForMember("ReportType", opt => opt.MapFrom(s => (int) s.Type));

            CreateMap<ApiFullTask, DtoTask>();
            CreateMap<ApiFullTask, DtoReport>();

            // .ForMember("ConnectionString", opt => opt.MapFrom(s => s.ConnectionString == "" ? null : s.ConnectionString));
            CreateMap<RTask, ApiTask>()
                .ForMember("RecepientGroupId", opt => opt.MapFrom(s => s.SendAddresses.Id))
                .ForMember("ScheduleId", opt => opt.MapFrom(s => s.Schedule.Id))
                .ForMember("ReportType", opt => opt.MapFrom(s => (int) s.Type));

            CreateMap<DtoFullInstance, DtoInstance>();
            CreateMap<DtoFullInstance, DtoInstanceData>()
                .ForMember("InstanceId", opt => opt.MapFrom(s => s.Id));
            CreateMap<DtoFullInstance, RFullInstance>()
                .ForMember("Data", opt => opt.Ignore())
                .ForMember("ViewData", opt => opt.Ignore());
        }
    }
}
