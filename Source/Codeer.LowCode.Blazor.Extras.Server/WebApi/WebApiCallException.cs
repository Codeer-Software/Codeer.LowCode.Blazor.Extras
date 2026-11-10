namespace Codeer.LowCode.Blazor.Extras.Server.WebApi
{
    //呼び出せなかった理由。Message はそのまま利用者に見せる (キー・トークン・キー入りの URL を入れない)
    sealed class WebApiCallException(string message) : Exception(message)
    {
    }
}
