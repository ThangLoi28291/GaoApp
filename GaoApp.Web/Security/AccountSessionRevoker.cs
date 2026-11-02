using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Web.Hubs;

namespace GaoApp.Web.Security;

public sealed class AccountSessionRevoker(RevocablePosHubLifetimeManager sessions) : IAccountSessionRevoker
{
    public void RevokeUser(int userId) => sessions.RevokeUser(userId);
}
