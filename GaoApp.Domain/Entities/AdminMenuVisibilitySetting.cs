using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

// Separate from Role/UserInStore: changing display preferences must not invalidate authentication stamps.
public sealed class AdminMenuVisibilitySetting : BaseStoreEntity
{
    public int? RoleId { get; set; }
    public int? UserInStoreId { get; set; }
    // Null means default/inherit. [] means an explicit configuration hiding nothing.
    public string? HiddenMenuIdsJson { get; set; }
}
