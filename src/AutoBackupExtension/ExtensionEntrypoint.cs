using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;

namespace AutoBackupExtension
{
	/// <summary>
	/// Extension entrypoint for the VisualStudio.Extensibility extension.
	/// </summary>
	[VisualStudioContribution]
	internal class ExtensionEntrypoint : Extension
	{
		/// <inheritdoc/>
		public override ExtensionConfiguration ExtensionConfiguration => new()
		{
			Metadata = new(
					id: "AutoBackupExtension.916652c0-a823-413b-a37e-ac317c535671",
					version: this.ExtensionAssemblyVersion,
					publisherName: "EXE",
					displayName: "AutoBackup Extension",
					description: "AutoBackup Extension"),
		};

		/// <inheritdoc />
		protected override void InitializeServices(IServiceCollection serviceCollection)
		{
			base.InitializeServices(serviceCollection);

			serviceCollection.AddSingleton(TimeProvider.System);
			serviceCollection.AddSingleton<BackupProvider>();
			serviceCollection.AddSingleton<BackupService>();
		}
	}
}
