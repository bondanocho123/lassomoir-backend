using Lassommoir.Domain;
using Lassommoir.Infrastructure.Persistence;

namespace Lassommoir.Api.Endpoints;

/// <summary>Batasi query ke data milik satu device; data device lain diperlakukan seperti tidak ada (404).</summary>
internal static class OwnershipQueries
{
    public static IQueryable<Project> ProjectsOf(this LassommoirDbContext db, Guid deviceId) =>
        db.Projects.Where(p => p.DeviceId == deviceId);

    public static IQueryable<TaskItem> TasksOf(this LassommoirDbContext db, Guid deviceId) =>
        db.Tasks.Where(t => db.Projects.Any(p => p.Id == t.ProjectId && p.DeviceId == deviceId));
}
