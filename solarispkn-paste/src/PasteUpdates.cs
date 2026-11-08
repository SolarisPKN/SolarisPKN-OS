// SPDX-License-Identifier: GPL-3.0-only
// Paste integration adapter. The actual updater and its UI live in a separate module.
using System.Reflection;
using SolarisPKN.Updates;

namespace SolarisPKN.Paste;

internal static class PasteUpdates
{
    public static UpdateProduct Product => new("solarispkn-paste",
        typeof(PasteUpdates).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+',2)[0] ?? "0.0.0");

    public static async Task<string?> CheckScheduledAsync(bool atLogin)
    {
        var product=Product;
        var update=await UpdatesClient.CheckIfDueAsync(product,atLogin);
        if(update is null)return null;
        var settings=UpdatePreferences.Load(product);
        if(settings.LastNotifiedVersion==update.Version)return null;
        settings.LastNotifiedVersion=update.Version;
        settings.Save(product);
        return update.Version;
    }

    public static void SelfTest() => UpdatesClient.SelfTest();
}
