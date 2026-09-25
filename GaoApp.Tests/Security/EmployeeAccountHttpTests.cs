using System.Net;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

public sealed class EmployeeAccountHttpTests
{
    private const string ChangePasswordUrl = "/admin/account/change-password";

    [Fact]
    public async Task Self_service_verifies_password_and_csrf_preserves_roles_and_revokes_sessions_across_stores()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var account = await app.AddAccountAsync(app.Stores[0], PermissionCodes.Catalog.Product.View);
        var other = await app.AddAccountAsync(app.Stores[1], PermissionCodes.Pos.Order.View, PermissionCodes.Catalog.Product.View);
        await using (var db = app.Database.CreateTenantContext(account.Store.StoreId))
        {
            var user = await db.Users.SingleAsync(x => x.Id == account.UserId);
            user.FullName = "Nguyễn Minh Lan"; user.Email = "minhlan@example.com";
            var membership = await db.UserInStores.SingleAsync(x => x.UserId == account.UserId);
            membership.PositionName = "Thu ngân"; membership.PhoneNumber = "0900000000";
            membership.Note = "Private manager note";
            (await db.Roles.SingleAsync(x => x.Id == account.RoleId)).Name = "Nhân viên bán hàng";
            await db.SaveChangesAsync();
        }
        await using (var db = app.Database.CreateTenantContext(other.Store.StoreId))
        {
            db.UserInStores.Add(new UserInStore { UserId = account.UserId, StoreId = other.Store.StoreId, RoleId = other.RoleId });
            await db.SaveChangesAsync();
        }
        using var own = await app.LoginAsync(account);
        using var sameStoreSession = await app.LoginAsync(account);
        using var secondStoreSession = await app.LoginAsync(account with { Store = other.Store, RoleId = other.RoleId });
        using var unrelated = await app.LoginAsync(other);
        using var anonymous = app.Anonymous(account.Store);
        using (var response = await anonymous.Http.PostAsync(ChangePasswordUrl, Form("wrong", "A new password 2026!")))
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using (var response = await own.Http.GetAsync(ChangePasswordUrl)) Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);

        var html = await own.Http.GetStringAsync("/admin/media-library");
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("Nguyễn Minh Lan", decoded);
        Assert.Contains("minhlan@example.com", decoded);
        Assert.Contains("Nhân viên bán hàng", decoded);
        Assert.Contains("Thu ngân", decoded);
        Assert.DoesNotContain("Private manager note", decoded);
        Assert.DoesNotContain("Đến quầy bán hàng", decoded);
        Assert.Single(Regex.Matches(html, "id=\"employeeAccountTrigger\""));
        Assert.Contains("employee-account.js", html);
        var posHtml = await secondStoreSession.Http.GetStringAsync("/admin/media-library");
        Assert.Contains("Đến quầy bán hàng", WebUtility.HtmlDecode(posHtml));
        var evidence = Path.Combine(FullApplicationFixture.SourceRoot(), "TestResults", "employee-account");
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, "admin.html"), posHtml);
        var posPage = await secondStoreSession.Http.GetStringAsync("/admin/pos");
        Assert.Single(Regex.Matches(posPage, "id=\"employeeAccountTrigger\""));
        Assert.Contains("posPrimeAdminDrawer", posPage);
        await File.WriteAllTextAsync(Path.Combine(evidence, "pos.html"), posPage);

        var token = own.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        own.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var response = await own.Http.PostAsync(ChangePasswordUrl, Form(account.Password, "A new password 2026!")))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        own.Http.DefaultRequestHeaders.Add("RequestVerificationToken", token);

        await Reject("wrong", "A new password 2026!", "CurrentPassword");
        await Reject(account.Password, "too-short", "NewPassword");
        await Reject(account.Password, new string('x', 129), "NewPassword");
        await Reject(account.Password, account.Password, "NewPassword");
        using (var response = await own.Http.PostAsync(ChangePasswordUrl, Form(account.Password, "A new password 2026!", "different confirmation")))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("ConfirmPassword", await response.Content.ReadAsStringAsync());
        }
        var newPassword = "Một câu mật khẩu mới 2026!";
        // Extra identity/permission fields must not change the target or grant permissions.
        using (var response = await own.Http.PostAsync(ChangePasswordUrl, Form(account.Password, newPassword,
            userId: other.UserId, roleId: other.RoleId)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("/admin/account/login", await response.Content.ReadAsStringAsync());
        }
        foreach (var client in new[] { own, sameStoreSession, secondStoreSession })
        {
            using var response = await client.Http.GetAsync("/admin/media-library/data");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using (var response = await unrelated.Http.GetAsync("/admin/media-library/data")) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using (var db = app.Database.CreateTenantContext(account.Store.StoreId))
        {
            var user = await db.Users.SingleAsync(x => x.Id == account.UserId);
            Assert.True(new PasswordHasher().Verify(newPassword, user.PasswordHash));
            Assert.False(new PasswordHasher().Verify(account.Password, user.PasswordHash));
            var otherUser = await db.Users.SingleAsync(x => x.Id == other.UserId);
            Assert.True(new PasswordHasher().Verify(other.Password, otherUser.PasswordHash));
            Assert.Equal(account.RoleId, (await db.UserInStores.SingleAsync(x => x.UserId == account.UserId)).RoleId);
        }
        using var refreshed = await app.LoginAsync(account with { Password = newPassword });
        using (var response = await refreshed.Http.GetAsync("/admin/media-library/data")) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var refreshedOtherStore = await app.LoginAsync(account with { Store = other.Store, RoleId = other.RoleId, Password = newPassword });
        using (var response = await refreshedOtherStore.Http.GetAsync("/admin/media-library/data")) Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        async Task Reject(string current, string next, string field)
        {
            using var response = await own.Http.PostAsync(ChangePasswordUrl, Form(current, next));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains(field, body);
            Assert.DoesNotContain(account.Password, body);
        }
    }

    [Fact]
    public async Task Credential_attempt_limit_is_shared_between_sessions_but_does_not_block_other_users()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var account = await app.AddAccountAsync(app.Stores[0], PermissionCodes.Catalog.Product.View);
        using var first = await app.LoginAsync(account);
        using var second = await app.LoginAsync(account);
        using var other = await app.LoginAsync(await app.AddAccountAsync(app.Stores[0], PermissionCodes.Catalog.Product.View));
        // Two successful logins consume two of the shared ten credential attempts.
        for (var i = 0; i < 8; i++)
        {
            using var response = await (i % 2 == 0 ? first : second).Http.PostAsync(ChangePasswordUrl, Form("incorrect", "A new password 2026!"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var blocked = await second.Http.PostAsync(ChangePasswordUrl, Form("incorrect", "A new password 2026!"));
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.NotNull(blocked.Headers.RetryAfter);
        using var unaffected = await other.Http.PostAsync(ChangePasswordUrl, Form("incorrect", "A new password 2026!"));
        Assert.Equal(HttpStatusCode.BadRequest, unaffected.StatusCode);
    }

    private static FormUrlEncodedContent Form(string current, string next, string? confirm = null, int userId = 0, int roleId = 0)
        => new(new Dictionary<string, string>
        {
            ["CurrentPassword"] = current, ["NewPassword"] = next, ["ConfirmPassword"] = confirm ?? next,
            ["UserId"] = userId.ToString(), ["RoleId"] = roleId.ToString()
        });
}
