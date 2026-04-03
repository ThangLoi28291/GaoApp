using Microsoft.AspNetCore.Identity;

namespace GaoApp.Infrastructure.Identity;

public class ApplicationUser : IdentityUser<int>
{
    // Sau này bạn có thể thêm FullName, Avatar...
}
