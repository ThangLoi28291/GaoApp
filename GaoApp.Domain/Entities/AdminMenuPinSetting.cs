using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

// Ordered references only. Labels, destinations, permissions and menu order stay in AdminMenuItems.
public sealed class AdminMenuPinSetting : BaseStoreEntity
{
    public string PinnedMenuIdsJson { get; set; } = "[]";
}
