// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.Items;
using Files.App.Utils.Storage;
using Files.Shared.Helpers;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text;
using System.Windows.Input;

namespace Files.App.ViewModels.UserControls
{
	public sealed partial class StatusBarViewModel : ObservableObject, IDisposable
	{
		private IContentPageContext ContentPageContext { get; } = Ioc.Default.GetRequiredService<IContentPageContext>();
		private IDevToolsSettingsService DevToolsSettingsService = Ioc.Default.GetRequiredService<IDevToolsSettingsService>();
		private readonly IStorageArchiveService StorageArchiveService = Ioc.Default.GetRequiredService<IStorageArchiveService>();
		private CurrentInstanceViewModel? InstanceViewModel => ContentPageContext.ShellPage?.InstanceViewModel;
		private CurrentInstanceViewModel? _subscribedInstanceViewModel;
		private bool isDisposed;

		private string? _DirectoryItemCount;
		public string? DirectoryItemCount
		{
			get => _DirectoryItemCount;
			set => SetProperty(ref _DirectoryItemCount, value);
		}

		private bool _IsZipEncodingSelectorVisible;
		public bool IsZipEncodingSelectorVisible
		{
			get => _IsZipEncodingSelectorVisible;
			set => SetProperty(ref _IsZipEncodingSelectorVisible, value);
		}

		public List<EncodingItem> ZipEncodingOptions { get; } = EncodingItem.Defaults.ToList();

		private EncodingItem? _SelectedZipEncoding;
		public EncodingItem? SelectedZipEncoding
		{
			get => _SelectedZipEncoding;
			set
			{
				if(value is null)
				{
					return;
				}
				if (SetProperty(ref _SelectedZipEncoding, value))
					_ = OnZipEncodingChangedAsync(value);
			}
		}

		public bool ShowOpenInIDEButton
		{
			get
			{
				return DevToolsSettingsService.OpenInIDEOption == OpenInIDEOption.AllLocations;
			}
		}

		public StatusBarViewModel()
		{
			DevToolsSettingsService.PropertyChanged += DevToolsSettingsService_PropertyChanged;
			SubscribeToShellPage();
			ContentPageContext.PropertyChanged += OnContentPageContextPropertyChanged;
		}

		private void DevToolsSettingsService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName is nameof(DevToolsSettingsService.OpenInIDEOption))
				OnPropertyChanged(nameof(ShowOpenInIDEButton));
		}

		private void OnContentPageContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(ContentPageContext.ShellPage))
			{
				UnsubscribeFromInstanceViewModel();
				SubscribeToShellPage();
			}
		}

		private void SubscribeToShellPage()
		{
			SubscribeToInstanceViewModel();
			_ = UpdateZipEncodingStateAsync();
		}

		public void Dispose()
		{
			if (isDisposed)
				return;

			isDisposed = true;
			DevToolsSettingsService.PropertyChanged -= DevToolsSettingsService_PropertyChanged;
			ContentPageContext.PropertyChanged -= OnContentPageContextPropertyChanged;
			UnsubscribeFromInstanceViewModel();
		}

		public async Task UpdateZipEncodingStateAsync()
		{
			var shellPage = ContentPageContext.ShellPage;
			if (shellPage?.SlimContentPage?.StatusBarViewModel != this)
				return;

			var instanceVM = InstanceViewModel;
			if (instanceVM is null)
				return;

			if (!instanceVM.IsPageTypeZipFolder)
			{
				IsZipEncodingSelectorVisible = false;
				return;
			}

			var workingDir = shellPage.GetRequiredShellViewModel().WorkingDirectory;
			if (string.IsNullOrEmpty(workingDir))
				return;
			var containerPath = ZipStorageFolder.GetContainerPath(workingDir);
			if (string.IsNullOrEmpty(containerPath) || !ZipStorageFolder.IsZipPath(containerPath))
				return;

			if (TryRestoreZipEncodingFromContainerPath(containerPath))
				return;

			try
			{
				var isUndetermined = await StorageArchiveService.IsEncodingUndeterminedAsync(containerPath);
				instanceVM.IsZipEncodingUndetermined = isUndetermined;

				if (!isUndetermined)
				{
					IsZipEncodingSelectorVisible = false;
					return;
				}

				var detected = await StorageArchiveService.DetectEncodingAsync(containerPath);
				if (detected is not null)
				{
					instanceVM.ZipEncodingName = detected.WebName;
					EncodingItem? ZipEncodingItem = ZipEncodingOptions.FirstOrDefault(e =>
						e.Encoding?.WebName.Equals(detected.WebName, StringComparison.OrdinalIgnoreCase) == true);
					if(ZipEncodingItem == null)
					{
						ZipEncodingItem = new EncodingItem(detected, detected.EncodingName);
					}
				    ZipEncodingOptions.Add(ZipEncodingItem);
					SelectedZipEncoding = ZipEncodingItem;
				}
				else
				{
					instanceVM.ZipEncodingName = null;
					SelectedZipEncoding = ZipEncodingOptions.FirstOrDefault(e => e.Encoding is null);
				}

				IsZipEncodingSelectorVisible = true;
			}
			catch (Exception ex)
			{
				App.Logger.LogError(ex, "Error checking zip encoding.");
				IsZipEncodingSelectorVisible = false;
			}
		}

		private bool TryRestoreZipEncodingFromContainerPath(string workingDir)
		{
			if (!FileExtensionHelpers.IsBrowsableZipFile(workingDir, out var ext))
				return false;

			var marker = workingDir.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
			if (marker is -1)
				return false;

			var containerPath = workingDir.Substring(0, marker + ext.Length);
			if (!ZipStorageFolder.TryGetEncodingForContainerPath(containerPath, out var encoding))
				return false;

			EncodingItem? match = encoding is not null
				? ZipEncodingOptions.FirstOrDefault(e => e.Encoding == encoding)
				: ZipEncodingOptions.FirstOrDefault(e => e.Encoding is null);

			if (match is null && encoding is not null)
			{
				match = new EncodingItem(encoding, encoding.EncodingName);
				ZipEncodingOptions.Add(match);
			}

			if (match is not null)
			{
				SelectedZipEncoding = match;
				IsZipEncodingSelectorVisible = true;
				return true;
			}

			return false;
		}

		private async Task OnZipEncodingChangedAsync(EncodingItem encodingItem)
		{
			var shellPage = ContentPageContext.ShellPage;
			if (shellPage is null)
				return;

			if (shellPage.SlimContentPage?.StatusBarViewModel != this)
				return;

			var workingDir = shellPage.GetRequiredShellViewModel().WorkingDirectory;
			if (string.IsNullOrEmpty(workingDir))
				return;

			if (FileExtensionHelpers.IsBrowsableZipFile(workingDir, out var ext))
			{
				var marker = workingDir.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
				if (marker is not -1)
				{
					var containerPath = workingDir.Substring(0, marker + ext.Length);

					ZipStorageFolder.TryGetEncodingForContainerPath(containerPath, out var oldEncoding);

					ZipStorageFolder.SetEncodingForContainerPath(containerPath, encodingItem.Encoding);

					var convertedPath = TryConvertZipPath(workingDir, containerPath, oldEncoding, encodingItem.Encoding);
					if (convertedPath is not null)
					{
						shellPage.NavigateToPath(convertedPath);
						return;
					}
				}
			}

			shellPage.GetRequiredShellViewModel().RefreshItems(null);
		}

		private static string? TryConvertZipPath(string workingDir, string containerPath, Encoding? oldEncoding, Encoding? newEncoding)
		{
			if (oldEncoding is null || newEncoding is null)
				return null;

			if (string.Equals(workingDir, containerPath, StringComparison.OrdinalIgnoreCase))
				return null;

			var relativePath = workingDir.Substring(containerPath.Length).Trim('\\', '/');
			if (string.IsNullOrEmpty(relativePath))
				return null;

			var components = relativePath.Split('\\', '/');

			try
			{
				var convertedComponents = new string[components.Length];
				for (var i = 0; i < components.Length; i++)
				{
					var rawBytes = oldEncoding.GetBytes(components[i]);
					convertedComponents[i] = newEncoding.GetString(rawBytes);
				}
				return containerPath + "\\" + string.Join("\\", convertedComponents) + "\\";
			}
			catch
			{
				return null;
			}
		}

		internal void SubscribeToInstanceViewModel()
		{
			var instanceVM = InstanceViewModel;
			if (instanceVM is not null)
			{
				instanceVM.PropertyChanged += OnInstanceViewModelPropertyChanged;
				_subscribedInstanceViewModel = instanceVM;
			}
		}

		internal void UnsubscribeFromInstanceViewModel()
		{
			if (_subscribedInstanceViewModel is not null)
				_subscribedInstanceViewModel.PropertyChanged -= OnInstanceViewModelPropertyChanged;
			_subscribedInstanceViewModel = null;
		}

		private void OnInstanceViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName is nameof(CurrentInstanceViewModel.IsPageTypeZipFolder))
			{
				_ = UpdateZipEncodingStateAsync();
			}
		}
	}
}
