using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop;

/// <summary>Limits this desktop's data to the signed-in user's scope rules (branches, departments, warehouses …) from sign-in on.</summary>
public static class DesktopRowScope
{
    /// <summary>Loads the user's row scope into the host so every later database context applies it.</summary>
    /// <param name="services">The application services.</param>
    /// <param name="userId">The signed-in user.</param>
    /// <returns>A task.</returns>
    public static async Task LoadAsync(IServiceProvider services, long userId)
    {
        RowScopeHolder holder = services.GetRequiredService<RowScopeHolder>();
        holder.Scope = null;
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        holder.Scope = await scope.ServiceProvider.GetRequiredService<IDataScopeService>().RowScopeAsync(userId).ConfigureAwait(true);
    }
}
