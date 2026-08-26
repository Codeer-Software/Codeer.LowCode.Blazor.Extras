using Extras.Client.Shared.Services;
using Microsoft.AspNetCore.Components;

namespace Extras.Client
{
    public class NavigationService : NavigationServiceBase
    {
        readonly NavigationManager _nav;
        readonly HttpClient _http;

        public NavigationService(NavigationManager nav, HttpClient http) : base(nav)
        {
            _nav = nav;
            _http = http;
        }

        //デモログイン (AccountController) のログアウト。サイドバーの Logout はこれで表示される
        public override bool CanLogout => true;

        public override async Task Logout()
        {
            await _http.PostAsync("api/account/logout", null);
            _nav.NavigateTo("/", true); //未ログインは login.html へリダイレクトされる
        }
    }
}
