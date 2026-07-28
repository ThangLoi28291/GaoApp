using GaoApp.Infrastructure.Data.Migrations;

namespace GaoApp.Migrator;

/// <summary>
/// Entry point của Migrator. Thứ tự fail-fast nằm trong pipeline có thể kiểm
/// thử độc lập mà không khởi tạo process hoặc truy cập database thật.
/// </summary>
public sealed class MigrationRunner
{
    private readonly MigrationExecutionPipeline _pipeline;

    public MigrationRunner(MigrationExecutionPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public Task RunAsync(CancellationToken ct = default)
        => _pipeline.RunAsync(ct);
}
