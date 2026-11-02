using GaoApp.Web.Services.Assistant;
using Xunit;

namespace GaoApp.Tests.Security;

public sealed partial class AssistantIntegrationTests
{
    private static AssistantPolicyKnowledge Policies(Fixture f) => new(new EnvironmentStub(f.DirectoryPath), f.Options, f.Clock);
    private static PolicyDraft Draft(Fixture f, string topic = "delivery", string body = "TEST: Chính sách thử nghiệm với đầy đủ điều kiện.", int offset = -1, int? end = null)
        => new(topic, "Chính sách TEST", body, f.Clock.Now.AddHours(offset), end is { } e ? f.Clock.Now.AddHours(e) : null, "Chứng từ thử nghiệm, cần quản trị duyệt");
    [Fact]
    public async Task Policy_drafts_are_immutable_reviewed_store_snapshots_with_idempotent_mutations() {
        using var f = new Fixture(); var service = Policies(f); var original = await service.ReadAsync(1, default); var request = Guid.NewGuid(); var draft = Draft(f);
        var row = await service.SaveAsync(1, 1, draft, original.Version, request, default);
        Assert.Null(AssistantPolicyKnowledge.Current(await service.ReadAsync(1,default), "delivery", f.Clock.Now));
        var replay = await service.SaveAsync(1, 1, draft, original.Version, request, default); Assert.Equal(row.Id, replay.Id);
        Assert.Equal(409, (await Assert.ThrowsAsync<AssistantException>(() => service.SaveAsync(1,1,draft with {Body="Changed"},original.Version,request,default))).Status);
        var current = await service.ReadAsync(1, default); var reviewRequest = Guid.NewGuid();
        await service.ReviewAsync(1,2,row.Id,"approve",current.Version,reviewRequest,true,default);
        Assert.Equal(row.Id,(await service.ReviewAsync(1,2,row.Id,"approve",current.Version,reviewRequest,true,default)).Id);
        var approved=AssistantPolicyKnowledge.Current(await service.ReadAsync(1,default),"delivery",f.Clock.Now)!; Assert.Equal(draft.Body,approved.Body);Assert.Equal(2,approved.ReviewedBy);
        Assert.Empty((await service.ReadAsync(2,default)).Revisions);
        var recreated=Policies(f);Assert.Equal(row.Id,AssistantPolicyKnowledge.Current(await recreated.ReadAsync(1,default),"delivery",f.Clock.Now)!.Id);
        var version=(await service.ReadAsync(1,default)).Version;
        Assert.Equal(404,(await Assert.ThrowsAsync<AssistantException>(()=>service.ReviewAsync(2,2,row.Id,"withdraw",Guid.Empty.ToString("N"),Guid.NewGuid(),true,default))).Status);
        Assert.Equal(version,(await service.ReadAsync(1,default)).Version);
    }
    [Fact]
    public async Task Scheduled_replacement_expires_without_resurrecting_old_policy_and_future_cancel_keeps_current() {
        using var f=new Fixture();var service=Policies(f);
        async Task<PolicyRevision> Publish(PolicyDraft draft) {
            var doc=await service.ReadAsync(1,default);var r=await service.SaveAsync(1,1,draft,doc.Version,Guid.NewGuid(),default);
            doc=await service.ReadAsync(1,default);return await service.ReviewAsync(1,2,r.Id,"approve",doc.Version,Guid.NewGuid(),true,default);
        }
        var old=await Publish(Draft(f,offset:-2));var scheduled=await Publish(Draft(f,offset:1,end:2));var doc=await service.ReadAsync(1,default);
        Assert.Equal(old.Id,AssistantPolicyKnowledge.Current(doc,"delivery",f.Clock.Now)!.Id);
        Assert.Equal(scheduled.Id,AssistantPolicyKnowledge.Current(doc,"delivery",f.Clock.Now.AddHours(1))!.Id);
        Assert.Null(AssistantPolicyKnowledge.Current(doc,"delivery",f.Clock.Now.AddHours(2)));
        await service.ReviewAsync(1,2,scheduled.Id,"withdraw",doc.Version,Guid.NewGuid(),false,default);
        doc=await service.ReadAsync(1,default);Assert.Equal(old.Id,AssistantPolicyKnowledge.Current(doc,"delivery",f.Clock.Now.AddHours(3))!.Id);
        var next=await Publish(Draft(f,offset:-1));doc=await service.ReadAsync(1,default);
        await service.ReviewAsync(1,2,next.Id,"withdraw",doc.Version,Guid.NewGuid(),false,default);
        Assert.Null(AssistantPolicyKnowledge.Current(await service.ReadAsync(1,default),"delivery",f.Clock.Now));
    }
    [Fact]
    public async Task Policy_approval_rejects_placeholders_expired_drafts_missing_confirmation_and_overlapping_schedule() {
        using var f=new Fixture();var service=Policies(f);
        foreach(var draft in new[]{Draft(f,body:"[CẦN XÁC NHẬN] Chưa có quy định thật."),Draft(f,offset:-2,end:-1),Draft(f)}) {
            var doc=await service.ReadAsync(1,default);var row=await service.SaveAsync(1,1,draft,doc.Version,Guid.NewGuid(),default);doc=await service.ReadAsync(1,default);
            var confirmed=draft.Body.Contains("CẦN")||draft.EffectiveUntil is not null;
            Assert.Equal(422,(await Assert.ThrowsAsync<AssistantException>(()=>service.ReviewAsync(1,2,row.Id,"approve",doc.Version,Guid.NewGuid(),confirmed,default))).Status);
        }
    }
    [Fact]
    public async Task Concurrent_policy_editors_cannot_overwrite_and_corrupt_cross_store_file_fails_closed() {
        using var f=new Fixture();var service=Policies(f);var version=(await service.ReadAsync(1,default)).Version;
        async Task<int> Save(AssistantPolicyKnowledge s) {try{await s.SaveAsync(1,1,Draft(f),version,Guid.NewGuid(),default);return 200;}catch(AssistantException e){return e.Status;}}
        var results=await Task.WhenAll(Save(service),Save(Policies(f)));Assert.Equal(new[]{200,409},results.Order().ToArray());
        var file=Path.Combine(f.DirectoryPath,f.Options.CurrentValue.StoreSettingsDirectory,"store-1-policies.json");
        var other=Path.Combine(Path.GetDirectoryName(file)!,"store-2-policies.json");File.Copy(file,other);
        Assert.Equal(503,(await Assert.ThrowsAsync<AssistantException>(()=>service.ReadAsync(2,default))).Status);
        File.WriteAllText(file,"{\"schemaVersion\":1,\"schemaVersion\":1}");
        Assert.Equal(503,(await Assert.ThrowsAsync<AssistantException>(()=>service.ReadAsync(1,default))).Status);
    }
}