﻿using Bit.Core.Enums;
using Bit.Core.Identity;
using Bit.Core.IdentityServer;
using Bit.Core.Models.Table;
using Bit.Core.Repositories;
using Bit.Core.Services;
using IdentityModel;
using IdentityServer4.Services;
using IdentityServer4.Stores;
using IdentityServer4.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
#if NET461
using Microsoft.WindowsAzure.Storage;
#endif
using System;
using System.IO;
using SqlServerRepos = Bit.Core.Repositories.SqlServer;

namespace Bit.Core.Utilities
{
    public static class ServiceCollectionExtensions
    {
        public static void AddSqlServerRepositories(this IServiceCollection services)
        {
            services.AddSingleton<IUserRepository, SqlServerRepos.UserRepository>();
            services.AddSingleton<ICipherRepository, SqlServerRepos.CipherRepository>();
            services.AddSingleton<IDeviceRepository, SqlServerRepos.DeviceRepository>();
            services.AddSingleton<IGrantRepository, SqlServerRepos.GrantRepository>();
            services.AddSingleton<IOrganizationRepository, SqlServerRepos.OrganizationRepository>();
            services.AddSingleton<IOrganizationUserRepository, SqlServerRepos.OrganizationUserRepository>();
            services.AddSingleton<ICollectionRepository, SqlServerRepos.CollectionRepository>();
            services.AddSingleton<IFolderRepository, SqlServerRepos.FolderRepository>();
            services.AddSingleton<ICollectionCipherRepository, SqlServerRepos.CollectionCipherRepository>();
            services.AddSingleton<IGroupRepository, SqlServerRepos.GroupRepository>();
            services.AddSingleton<IU2fRepository, SqlServerRepos.U2fRepository>();
        }

        public static void AddBaseServices(this IServiceCollection services)
        {
            services.AddSingleton<ICipherService, CipherService>();
            services.AddScoped<IUserService, UserService>();
            services.AddSingleton<IDeviceService, DeviceService>();
            services.AddSingleton<IOrganizationService, OrganizationService>();
            services.AddSingleton<ICollectionService, CollectionService>();
            services.AddSingleton<IGroupService, GroupService>();
        }

        public static void AddDefaultServices(this IServiceCollection services, GlobalSettings globalSettings)
        {
            services.AddSingleton<IMailService, RazorViewMailService>();

            if(CoreHelpers.SettingHasValue(globalSettings.Mail.SendGridApiKey))
            {
                services.AddSingleton<IMailDeliveryService, SendGridMailDeliveryService>();
            }
            else if(CoreHelpers.SettingHasValue(globalSettings.Mail?.Smtp?.Host) &&
                CoreHelpers.SettingHasValue(globalSettings.Mail?.Smtp?.Username) &&
                CoreHelpers.SettingHasValue(globalSettings.Mail?.Smtp?.Password))
            {
                services.AddSingleton<IMailDeliveryService, SmtpMailDeliveryService>();
            }
            else
            {
                services.AddSingleton<IMailDeliveryService, NoopMailDeliveryService>();
            }

#if NET461
            if(globalSettings.SelfHosted)
            {
                services.AddSingleton<IPushNotificationService, NoopPushNotificationService>();
                services.AddSingleton<IPushRegistrationService, NoopPushRegistrationService>();
            }
            else
            {
                services.AddSingleton<IPushNotificationService, NotificationHubPushNotificationService>();
                services.AddSingleton<IPushRegistrationService, NotificationHubPushRegistrationService>();
            }
#else
            services.AddSingleton<IPushNotificationService, NoopPushNotificationService>();
            services.AddSingleton<IPushRegistrationService, NoopPushRegistrationService>();
#endif

            if(CoreHelpers.SettingHasValue(globalSettings.Storage.ConnectionString))
            {
                services.AddSingleton<IBlockIpService, AzureQueueBlockIpService>();
            }
            else
            {
                services.AddSingleton<IBlockIpService, NoopBlockIpService>();
            }

            if(CoreHelpers.SettingHasValue(globalSettings.Attachment.ConnectionString))
            {
                services.AddSingleton<IAttachmentStorageService, AzureAttachmentStorageService>();
            }
            else if(CoreHelpers.SettingHasValue(globalSettings.Attachment.BaseDirectory))
            {
                services.AddSingleton<IAttachmentStorageService, LocalAttachmentStorageService>();
            }
            else
            {
                services.AddSingleton<IAttachmentStorageService, NoopAttachmentStorageService>();
            }
        }

        public static void AddNoopServices(this IServiceCollection services)
        {
            services.AddSingleton<IMailService, NoopMailService>();
            services.AddSingleton<IMailDeliveryService, NoopMailDeliveryService>();
            services.AddSingleton<IPushNotificationService, NoopPushNotificationService>();
            services.AddSingleton<IBlockIpService, NoopBlockIpService>();
            services.AddSingleton<IPushRegistrationService, NoopPushRegistrationService>();
            services.AddSingleton<IAttachmentStorageService, NoopAttachmentStorageService>();
        }

        public static IdentityBuilder AddCustomIdentityServices(
            this IServiceCollection services, GlobalSettings globalSettings)
        {
            services.AddTransient<ILookupNormalizer, LowerInvariantLookupNormalizer>();

            services.Configure<TwoFactorRememberTokenProviderOptions>(options =>
            {
                options.TokenLifespan = TimeSpan.FromDays(30);
            });

            var identityBuilder = services.AddIdentity<User, Role>(options =>
            {
                options.User = new UserOptions
                {
                    RequireUniqueEmail = true,
                    AllowedUserNameCharacters = null // all
                };
                options.Password = new PasswordOptions
                {
                    RequireDigit = false,
                    RequireLowercase = false,
                    RequiredLength = 8,
                    RequireNonAlphanumeric = false,
                    RequireUppercase = false
                };
                options.ClaimsIdentity = new ClaimsIdentityOptions
                {
                    SecurityStampClaimType = "sstamp",
                    UserNameClaimType = JwtClaimTypes.Email,
                    UserIdClaimType = JwtClaimTypes.Subject,
                };
                options.Tokens.ChangeEmailTokenProvider = TokenOptions.DefaultEmailProvider;
            });

            identityBuilder
                .AddUserStore<UserStore>()
                .AddRoleStore<RoleStore>()
                .AddTokenProvider<DataProtectorTokenProvider<User>>(TokenOptions.DefaultProvider)
                .AddTokenProvider<AuthenticatorTokenProvider>(TwoFactorProviderType.Authenticator.ToString())
                .AddTokenProvider<YubicoOtpTokenProvider>(TwoFactorProviderType.YubiKey.ToString())
                .AddTokenProvider<DuoWebTokenProvider>(TwoFactorProviderType.Duo.ToString())
                .AddTokenProvider<U2fTokenProvider>(TwoFactorProviderType.U2f.ToString())
                .AddTokenProvider<TwoFactorRememberTokenProvider>(TwoFactorProviderType.Remember.ToString())
                .AddTokenProvider<EmailTokenProvider<User>>(TokenOptions.DefaultEmailProvider);

            return identityBuilder;
        }

        public static IIdentityServerBuilder AddCustomIdentityServerServices(
            this IServiceCollection services, IHostingEnvironment env, GlobalSettings globalSettings)
        {
            var identityServerBuilder = services
                .AddIdentityServer(options =>
                {
                    options.Endpoints.EnableAuthorizeEndpoint = false;
                    options.Endpoints.EnableIntrospectionEndpoint = false;
                    options.Endpoints.EnableEndSessionEndpoint = false;
                    options.Endpoints.EnableUserInfoEndpoint = false;
                    options.Endpoints.EnableCheckSessionEndpoint = false;
                    options.Endpoints.EnableTokenRevocationEndpoint = false;
                    options.IssuerUri = globalSettings.BaseServiceUri.InternalIdentity;
                })
                .AddInMemoryApiResources(ApiResources.GetApiResources())
                .AddInMemoryClients(Clients.GetClients());

            services.AddTransient<ICorsPolicyService, AllowAllCorsPolicyService>();

            if(env.IsDevelopment())
            {
                identityServerBuilder.AddTemporarySigningCredential();
            }
            else if(!string.IsNullOrWhiteSpace(globalSettings.IdentityServer.CertificatePassword)
                && File.Exists("identity.pfx"))
            {
                var identityServerCert = CoreHelpers.GetCertificate("identity.pfx",
                    globalSettings.IdentityServer.CertificatePassword);
                identityServerBuilder.AddSigningCredential(identityServerCert);
            }
            else if(!string.IsNullOrWhiteSpace(globalSettings.IdentityServer.CertificateThumbprint))
            {
                var identityServerCert = CoreHelpers.GetCertificate(globalSettings.IdentityServer.CertificateThumbprint);
                identityServerBuilder.AddSigningCredential(identityServerCert);
            }
            else
            {
                throw new Exception("No identity certificate to use.");
            }

            services.AddScoped<IResourceOwnerPasswordValidator, ResourceOwnerPasswordValidator>();
            services.AddScoped<IProfileService, ProfileService>();
            services.AddSingleton<IPersistedGrantStore, PersistedGrantStore>();

            return identityServerBuilder;
        }

        public static void AddCustomDataProtectionServices(
            this IServiceCollection services, IHostingEnvironment env, GlobalSettings globalSettings)
        {
            if(env.IsDevelopment())
            {
                return;
            }

            if(globalSettings.SelfHosted && CoreHelpers.SettingHasValue(globalSettings.DataProtection.Directory))
            {
                services.AddDataProtection()
                    .PersistKeysToFileSystem(new DirectoryInfo(globalSettings.DataProtection.Directory));
            }

#if NET461
            if(!globalSettings.SelfHosted)
            {
                var dataProtectionCert = CoreHelpers.GetCertificate(globalSettings.DataProtection.CertificateThumbprint);
                var storageAccount = CloudStorageAccount.Parse(globalSettings.Storage.ConnectionString);
                services.AddDataProtection()
                    .PersistKeysToAzureBlobStorage(storageAccount, "aspnet-dataprotection/keys.xml")
                    .ProtectKeysWithCertificate(dataProtectionCert);
            }
#endif
        }

        public static GlobalSettings AddGlobalSettingsServices(this IServiceCollection services,
            IConfigurationRoot root)
        {
            var globalSettings = new GlobalSettings();
            ConfigurationBinder.Bind(root.GetSection("GlobalSettings"), globalSettings);
            services.AddSingleton(s => globalSettings);
            return globalSettings;
        }

        public static void UseForwardedHeadersForAzure(this IApplicationBuilder app)
        {
            // ref: https://github.com/aspnet/Docs/issues/2384
            var forwardOptions = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
                RequireHeaderSymmetry = false
            };

            forwardOptions.KnownNetworks.Clear();
            forwardOptions.KnownProxies.Clear();

            app.UseForwardedHeaders(forwardOptions);
        }
    }
}
