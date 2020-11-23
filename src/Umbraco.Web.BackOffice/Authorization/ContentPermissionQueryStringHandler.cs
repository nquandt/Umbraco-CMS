﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Primitives;
using System;
using System.Threading.Tasks;
using Umbraco.Core;
using Umbraco.Core.Models;
using Umbraco.Core.Security;
using Umbraco.Core.Services;

namespace Umbraco.Web.BackOffice.Authorization
{
    /// <summary>
    /// Used to authorize if the user has the correct permission access to the content for the <see cref="IContent"/> specified
    /// </summary>
    public class ContentPermissionResourceHandler : AuthorizationHandler<ContentPermissionResourceRequirement, IContent>
    {
        private readonly IBackOfficeSecurityAccessor _backofficeSecurityAccessor;
        private readonly IEntityService _entityService;
        private readonly IUserService _userService;

        public ContentPermissionResourceHandler(
            IBackOfficeSecurityAccessor backofficeSecurityAccessor,
            IEntityService entityService,
            IUserService userService)
        {
            _backofficeSecurityAccessor = backofficeSecurityAccessor;
            _entityService = entityService;
            _userService = userService;
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ContentPermissionResourceRequirement requirement, IContent resource)
        {
            var permissionResult = ContentPermissionsHelper.CheckPermissions(resource,
                _backofficeSecurityAccessor.BackOfficeSecurity.CurrentUser,
                _userService,
                _entityService,
                new[] { requirement.PermissionToCheck });

            if (permissionResult == ContentPermissionsHelper.ContentAccess.Denied)
            {
                context.Fail();
            }
            else
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Used to authorize if the user has the correct permission access to the content for the content id specified in a query string
    /// </summary>
    public class ContentPermissionQueryStringHandler : AuthorizationHandler<ContentPermissionsQueryStringRequirement>
    {
        private readonly IBackOfficeSecurityAccessor _backofficeSecurityAccessor;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IEntityService _entityService;
        private readonly IUserService _userService;
        private readonly IContentService _contentService;

        public ContentPermissionQueryStringHandler(
            IBackOfficeSecurityAccessor backofficeSecurityAccessor,
            IHttpContextAccessor httpContextAccessor, 
            IEntityService entityService,
            IUserService userService,
            IContentService contentService)
        {
            _backofficeSecurityAccessor = backofficeSecurityAccessor;
            _httpContextAccessor = httpContextAccessor;
            _entityService = entityService;
            _userService = userService;
            _contentService = contentService;
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ContentPermissionsQueryStringRequirement requirement)
        {
            int nodeId;
            if (requirement.NodeId.HasValue == false)
            {
                StringValues routeVal;
                foreach(var qs in requirement.QueryStringNames)
                {
                    if (_httpContextAccessor.HttpContext.Request.Query.TryGetValue(qs, out routeVal))
                    {
                        break;                        
                    }
                }

                if (routeVal.Count == 0)
                {
                    throw new InvalidOperationException("No argument found for the current action with by names " + string.Join(", ", requirement.QueryStringNames));
                }
                
                var argument = routeVal.ToString();
                // if the argument is an int, it will parse and can be assigned to nodeId
                // if might be a udi, so check that next
                // otherwise treat it as a guid - unlikely we ever get here
                if (int.TryParse(argument, out int parsedId))
                {
                    nodeId = parsedId;
                }
                else if (UdiParser.TryParse(argument, true, out var udi))
                {
                    nodeId = _entityService.GetId(udi).Result;
                }
                else
                {
                    Guid.TryParse(argument, out Guid key);
                    nodeId = _entityService.GetId(key, UmbracoObjectTypes.Document).Result;
                }
            }
            else
            {
                nodeId = requirement.NodeId.Value;
            }

            var permissionResult = ContentPermissionsHelper.CheckPermissions(nodeId,
                _backofficeSecurityAccessor.BackOfficeSecurity.CurrentUser,
                _userService,
                _contentService,
                _entityService,
                out var contentItem,
                new[] { requirement.PermissionToCheck });

            if (permissionResult == ContentPermissionsHelper.ContentAccess.NotFound)
            {
                return null;
            }

            if (permissionResult == ContentPermissionsHelper.ContentAccess.Denied)
            {
                context.Fail();
            }
            else
            {
                context.Succeed(requirement);
            }


            if (contentItem != null)
            {
                //store the content item in request cache so it can be resolved in the controller without re-looking it up
                _httpContextAccessor.HttpContext.Items[typeof(IContent).ToString()] = contentItem;
            }

            return Task.CompletedTask;
        }

    }
}
