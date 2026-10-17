using Microsoft.UI.Reactor;
using SegmentedXamlMetadataProvider = CommunityToolkit.WinUI.Controls.SegmentedRns.CommunityToolkit_WinUI_Controls_Segmented_XamlTypeInfo.XamlMetaDataProvider;
using SettingsXamlMetadataProvider = CommunityToolkit.WinUI.Controls.SettingsControlsRns.CommunityToolkit_WinUI_Controls_SettingsControls_XamlTypeInfo.XamlMetaDataProvider;
using TokenizingXamlMetadataProvider = CommunityToolkit.WinUI.Controls.TokenizingTextBoxRns.CommunityToolkit_WinUI_Controls_TokenizingTextBox_XamlTypeInfo.XamlMetaDataProvider;

namespace Tonarink.Controls;

static class ToolkitXamlMetadata
{
    public static void Register()
    {
        ReactorApp.RegisterControlAssembly(new SegmentedXamlMetadataProvider());
        ReactorApp.RegisterControlAssembly(new SettingsXamlMetadataProvider());
        ReactorApp.RegisterControlAssembly(new TokenizingXamlMetadataProvider());
    }
}
