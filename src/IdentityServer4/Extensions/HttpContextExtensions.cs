﻿// Copyright (c) Brock Allen & Dominick Baier. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.


using IdentityServer4.Configuration;
using IdentityServer4.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using IdentityServer4.Models;
using IdentityServer4.Stores;
using System.Linq;

#pragma warning disable 1591

namespace IdentityServer4.Extensions
{
    public static class HttpContextExtensions
    {
        public static void SetIdentityServerOrigin(this HttpContext context, string value)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            context.Items[Constants.EnvironmentKeys.IdentityServerOrigin] = value;
        }

        public static void SetIdentityServerBasePath(this HttpContext context, string value)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            context.Items[Constants.EnvironmentKeys.IdentityServerBasePath] = value;
        }

        public static string GetIdentityServerOrigin(this HttpContext context)
        {
            return context.Items[Constants.EnvironmentKeys.IdentityServerOrigin] as string;
        }

        /// <summary>
        /// Gets the base path of IdentityServer. Can be used inside of Katana <c>Map</c>ped middleware.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns></returns>
        public static string GetIdentityServerBasePath(this HttpContext context)
        {
            return context.Items[Constants.EnvironmentKeys.IdentityServerBasePath] as string;
        }

        /// <summary>
        /// Gets the public base URL for IdentityServer.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns></returns>
        public static string GetIdentityServerBaseUrl(this HttpContext context)
        {
            return context.GetIdentityServerOrigin() + context.GetIdentityServerBasePath();
        }

        /// <summary>
        /// Gets the identity server relative URL.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="path">The path.</param>
        /// <returns></returns>
        public static string GetIdentityServerRelativeUrl(this HttpContext context, string path)
        {
            if (!path.IsLocalUrl())
            {
                return null;
            }

            if (path.StartsWith("~/")) path = path.Substring(1);
            path = context.GetIdentityServerBaseUrl().EnsureTrailingSlash() + path.RemoveLeadingSlash();
            return path;
        }

        /// <summary>
        /// Gets the identity server issuer URI.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns></returns>
        /// <exception cref="System.ArgumentNullException">context</exception>
        public static string GetIdentityServerIssuerUri(this HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // if they've explicitly configured a URI then use it,
            // otherwise dynamically calculate it
            var options = context.RequestServices.GetRequiredService<IdentityServerOptions>();
            var uri = options.IssuerUri;
            if (uri.IsMissing())
            {
                uri = context.GetIdentityServerBaseUrl();
                if (uri.EndsWith("/")) uri = uri.Substring(0, uri.Length - 1);
                uri = uri.ToLowerInvariant();
            }

            return uri;
        }

        /// <summary>
        /// Gets the identity server user asynchronous.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns></returns>
        public static async Task<ClaimsPrincipal> GetIdentityServerUserAsync(this HttpContext context)
        {
            var userSession = context.RequestServices.GetRequiredService<IUserSession>();
            var user = await userSession.GetIdentityServerUserAsync();
            return user;
        }

        internal static async Task<string> GetIdentityServerSignoutFrameCallbackUrlAsync(this HttpContext context, LogoutMessage logoutMessage = null)
        {
            var userSession = context.RequestServices.GetRequiredService<IUserSession>();
            var user = await userSession.GetIdentityServerUserAsync();
            var currentSubId = user?.GetSubjectId();

            EndSession endSessionMsg = null;

            // if we have a logout message, then that take precedence over the current user
            if (logoutMessage?.ClientIds?.Any() == true)
            {
                var clientIds = logoutMessage?.ClientIds;

                // check if current user is same, since we migth have new clients (albeit unlikely)
                if (currentSubId == logoutMessage?.SubjectId)
                {
                    clientIds = clientIds.Union(await userSession.GetClientListAsync());
                    clientIds = clientIds.Distinct();
                }

                endSessionMsg = new EndSession
                {
                    SubjectId = logoutMessage.SubjectId,
                    SessionId = logoutMessage.SessionId,
                    ClientIds = clientIds
                };
            }
            else if (currentSubId != null)
            {
                // see if current user has any clients they need to signout of 
                var clientIds = await userSession.GetClientListAsync();
                if (clientIds.Any())
                {
                    endSessionMsg = new EndSession
                    {
                        SubjectId = currentSubId,
                        SessionId = await userSession.GetCurrentSessionIdAsync(),
                        ClientIds = clientIds
                    };
                }
            }

            if (endSessionMsg != null)
            {
                var msg = new Message<EndSession>(endSessionMsg);

                var endSessionMessageStore = context.RequestServices.GetRequiredService<IMessageStore<EndSession>>();
                var id = await endSessionMessageStore.WriteAsync(msg);

                var signoutIframeUrl = context.GetIdentityServerBaseUrl().EnsureTrailingSlash() + Constants.ProtocolRoutePaths.EndSessionCallback;
                signoutIframeUrl = signoutIframeUrl.AddQueryString(Constants.UIConstants.DefaultRoutePathParams.EndSessionCallback, id);

                return signoutIframeUrl;
            }

            // no sessions, so nothing to cleanup
            return null;
        }
    }
}