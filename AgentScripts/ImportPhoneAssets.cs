// Re-imports the phone's UI, the runtime theme and the edited input actions (decision 0047): the unfocused Editor does not import
// non-script assets edited on disk. Run with run_script.
using UnityEditor;

public static class ImportPhoneAssets
{
    public static string Run()
    {
        foreach (var path in new[]
                 {
                     "Assets/UI/Resources/UI/Phone/Phone.uxml", "Assets/UI/Resources/UI/Phone/Phone.uss", "Assets/UI/DevRuntimeTheme.tss", "Assets/UI/Resources/UI/Logistics/Logistics.uss",
                     "Assets/InputSystem_Actions.inputactions", "Assets/Prefabs/Player/Player.prefab"
                 })
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return "imported";
    }
}
