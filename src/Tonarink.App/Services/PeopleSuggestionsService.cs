using System.Globalization;
using Microsoft.Extensions.Logging;
using Windows.ApplicationModel.Contacts;
using Windows.ApplicationModel.UserDataAccounts;
using Windows.Foundation.Metadata;
using Windows.Storage.Streams;

namespace Tonarink.Services;

static class PeopleSuggestionsService
{
    private const string PeopleContractName = "com.microsoft.peoplecontract";
    private const string WindowsSystemPackageFamilyName = "com.microsoft.windows.system";
    private const string ContactListName = "Tonarink favorites";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly string SyncMarkerPath = Path.Combine(
        AppPlatform.DataDirectory,
        "people-suggestions.enabled");

    public static async Task SynchronizeAsync(
        bool enabled,
        IReadOnlyCollection<FavoriteDevice> favorites,
        CancellationToken cancellationToken = default)
    {
        if (!AppPlatform.HasPackageIdentity()
            || !ApiInformation.IsTypePresent("Windows.ApplicationModel.Contacts.ContactManager"))
            return;
        if (!enabled && !File.Exists(SyncMarkerPath))
            return;

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var accountStore = await UserDataAccountManager.RequestStoreAsync(
                UserDataAccountStoreAccessType.AppAccountsReadWrite);
            if (accountStore is null)
                return;

            var accounts = await accountStore.FindAccountsAsync();
            var account = accounts.FirstOrDefault(static account =>
                string.Equals(account.UserDisplayName, PeopleContractName, StringComparison.Ordinal));

            if (!enabled)
            {
                if (account is not null)
                    await DeleteAccountDataAsync(account).ConfigureAwait(false);
                File.Delete(SyncMarkerPath);
                return;
            }

            account ??= await accountStore.CreateAccountAsync(PeopleContractName);
            if (!account.ExplictReadAccessPackageFamilyNames.Contains(WindowsSystemPackageFamilyName))
            {
                account.ExplictReadAccessPackageFamilyNames.Add(WindowsSystemPackageFamilyName);
                await account.SaveAsync();
            }

            Directory.CreateDirectory(AppPlatform.DataDirectory);
            File.WriteAllText(SyncMarkerPath, account.Id);
            var annotationCount = await ReplaceContactsAsync(
                    account,
                    favorites,
                    cancellationToken)
                .ConfigureAwait(false);
            AppDiagnostics.Write(
                LogLevel.Information,
                "people-suggestions",
                $"Synchronized {favorites.Count} favorite contact(s) and {annotationCount} share annotation(s). " +
                $"AccountId={account.Id}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer favorite/settings snapshot superseded this synchronization.
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write(
                LogLevel.Warning,
                "people-suggestions",
                "Could not synchronize favorite devices with Windows People.",
                exception);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<int> ReplaceContactsAsync(
        UserDataAccount account,
        IReadOnlyCollection<FavoriteDevice> favorites,
        CancellationToken cancellationToken)
    {
        var contactStore = await ContactManager.RequestStoreAsync(ContactStoreAccessType.AppContactsReadWrite);
        if (contactStore is null)
            throw new InvalidOperationException("Windows did not grant access to the app contact store.");

        await DeleteContactListsAsync(contactStore, account.Id).ConfigureAwait(false);
        await DeleteAnnotationListsAsync(account.Id).ConfigureAwait(false);

        if (favorites.Count == 0)
            return 0;

        var contactList = await contactStore.CreateContactListAsync(ContactListName, account.Id);
        contactList.OtherAppReadAccess = ContactListOtherAppReadAccess.None;
        await contactList.SaveAsync();

        foreach (var favorite in favorites.OrderBy(static favorite => favorite.Name, StringComparer.CurrentCulture))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var contact = new Contact
            {
                FirstName = favorite.Name,
                RemoteId = favorite.Fingerprint,
                SourceDisplayPicture = RandomAccessStreamReference.CreateFromUri(
                    new Uri("ms-appx:///Assets/Square150x150Logo.png")),
            };
            await contactList.SaveContactAsync(contact);
        }

        // People uses Share annotations and their ranks to populate the suggestions row.
        // AppAnnotationsReadWrite is intentionally requested with only the ordinary
        // `contacts` capability; failures are logged by the caller and never escalate
        // Tonarink to the restricted contactsSystem capability.
        var annotationStore = await ContactManager.RequestAnnotationStoreAsync(
            ContactAnnotationStoreAccessType.AppAnnotationsReadWrite);
        if (annotationStore is null)
            throw new InvalidOperationException("Windows did not grant access to app contact annotations.");

        var annotationList = await annotationStore.CreateAnnotationListAsync(account.Id);
        var rank = favorites.Count;
        var annotationCount = 0;
        foreach (var favorite in favorites.OrderBy(static favorite => favorite.Name, StringComparer.CurrentCulture))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var contact = await contactList.GetContactFromRemoteIdAsync(favorite.Fingerprint);
            if (contact is null)
                continue;

            var annotation = new ContactAnnotation
            {
                ContactId = contact.Id,
                SupportedOperations = ContactAnnotationOperations.Share,
            };
            annotation.ProviderProperties["Rank"] = rank--.ToString(CultureInfo.InvariantCulture);
            if (!await annotationList.TrySaveAnnotationAsync(annotation))
                throw new InvalidOperationException($"Windows rejected the share annotation for '{favorite.Name}'.");
            annotationCount++;
        }

        // Commit the completed list after its contacts and annotations have been
        // populated so the Windows People aggregator refreshes the suggestions row.
        await contactList.SaveAsync();

        return annotationCount;
    }

    private static async Task DeleteAccountDataAsync(UserDataAccount account)
    {
        var contactStore = await ContactManager.RequestStoreAsync(ContactStoreAccessType.AppContactsReadWrite);
        if (contactStore is not null)
            await DeleteContactListsAsync(contactStore, account.Id).ConfigureAwait(false);
        await DeleteAnnotationListsAsync(account.Id).ConfigureAwait(false);
        await account.DeleteAsync();
    }

    private static async Task DeleteContactListsAsync(ContactStore store, string accountId)
    {
        var lists = await store.FindContactListsAsync();
        foreach (var list in lists.Where(list =>
                     string.Equals(list.UserDataAccountId, accountId, StringComparison.Ordinal)))
            await list.DeleteAsync();
    }

    private static async Task DeleteAnnotationListsAsync(string accountId)
    {
        var annotationStore = await ContactManager.RequestAnnotationStoreAsync(
            ContactAnnotationStoreAccessType.AppAnnotationsReadWrite);
        if (annotationStore is null)
            return;

        var lists = await annotationStore.FindAnnotationListsAsync();
        foreach (var list in lists.Where(list =>
                     string.Equals(list.UserDataAccountId, accountId, StringComparison.Ordinal)))
            await list.DeleteAsync();
    }
}
