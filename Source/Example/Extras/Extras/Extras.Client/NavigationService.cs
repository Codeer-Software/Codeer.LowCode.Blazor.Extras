using Codeer.LowCode.Blazor.RequestInterfaces;
using Extras.Client.Shared.Services;
using Microsoft.AspNetCore.Components;

namespace Extras.Client
{
    public class NavigationService : NavigationServiceBase
    {
        public NavigationService(NavigationManager nav, IAppInfoService app) : base(nav, app) { }
        public override bool CanLogout => false;
        public override async Task Logout() => await Task.CompletedTask;
    }
}
