﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestPlatform.TestHost;

using TeamServer.Interfaces;
using TeamServer.Services;

namespace TeamServer.UnitTests
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IUserService, UserService>();
            services.AddSingleton<SharpC2Service>();
            services.AddSingleton<ICryptoService, CryptoService>();
            services.AddSingleton<ICredentialService, CredentialService>();

            services.AddAutoMapper(typeof(Program));
            services.AddSignalR();
        }
    }
}