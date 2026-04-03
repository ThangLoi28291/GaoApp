namespace GaoApp.Application.Common.Interfaces;

/// <summary>
/// Abstraction cache dùng cho Application layer.
/// Application chỉ biết interface này, không phụ thuộc trực tiếp IMemoryCache / Redis.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Lấy dữ liệu từ cache nếu có.
    /// Nếu chưa có thì gọi factory để tạo dữ liệu, lưu cache rồi trả về.
    /// </summary>
    Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan? expiry = null);

    /// <summary>
    /// Xóa 1 key cache cụ thể.
    /// </summary>
    void Remove(string key);
}