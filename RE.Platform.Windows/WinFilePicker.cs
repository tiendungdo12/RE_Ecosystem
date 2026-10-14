using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using RE.Core.Abstractions;

namespace RE.Platform.Windows
{
    public class WinFilePicker : IFilePicker
    {
        public static IntPtr WindowHandle { get; set; }

        public async Task<string?> PickFileAsync()
        {
            var picker = new FileOpenPicker();
            if (WindowHandle != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHandle);
            }

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add("*");

            var file = await picker.PickSingleFileAsync();
            return file?.Path;
        }
    }
}