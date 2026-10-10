# 旧测试逐案例迁移清单

日期：2026-10-07

清点来源：当前仓库 HEAD 的旧测试声明。下面按方法标识列举，参数化行仍由 MSTest DataRow 保留；方法数量不等于实际执行数量。运行证据见 [testing-progress.md](testing-progress.md)。

迁移审计：106 条保留或重写的当前位置映射均已核对实际方法和通过的 TRX 结果，未发现缺失项。多个旧结构检查可映射到同一个真实行为案例，因此这个数字不是新的测试数量，也不代表目标覆盖矩阵已完成。

## tests/LocalSendDotNet.Core.Mobile.Tests/PortableServerTests.cs

| 原方法 | 处理 | 当前位置/方法 | 说明 |
| --- | --- | --- | --- |
| `TwoPortableNodesTransferTextOverMutualTls` | 保留 | `LocalSendDotNet.Core.Mobile.Tests/PortableServerTests.cs` → `TwoPortableNodesTransferTextOverMutualTls` | 迁移为 MSTest；参数化行保留 |
| `InfoEndpointAllowsBrowserWithoutClientCertificate` | 保留 | `LocalSendDotNet.Core.Mobile.Tests/PortableServerTests.cs` → `InfoEndpointAllowsBrowserWithoutClientCertificate` | 迁移为 MSTest；参数化行保留 |
| `PortableServerSupportsPinProtectedTransfer` | 保留 | `LocalSendDotNet.Core.Mobile.Tests/PortableServerTests.cs` → `PortableServerSupportsPinProtectedTransfer` | 迁移为 MSTest；参数化行保留 |
| `PortableServerSupportsPartialAcceptance` | 保留 | `LocalSendDotNet.Core.Mobile.Tests/PortableServerTests.cs` → `PortableServerSupportsPartialAcceptance` | 迁移为 MSTest；参数化行保留 |

## tests/LocalSendDotNet.Core.Tests/IdentityTests.cs

| 原方法 | 处理 | 当前位置/方法 | 说明 |
| --- | --- | --- | --- |
| `IdentityIsStableAcrossReloads` | 保留 | `LocalSendDotNet.Core.Tests/IdentityTests.cs` → `IdentityIsStableAcrossReloads` | 迁移为 MSTest；参数化行保留 |
| `PeerFingerprintMustMatch` | 保留 | `LocalSendDotNet.Core.Tests/IdentityTests.cs` → `PeerFingerprintMustMatch` | 迁移为 MSTest；参数化行保留 |
| `IncompleteIdentityProducesActionableException` | 保留 | `LocalSendDotNet.Core.Tests/IdentityTests.cs` → `IncompleteIdentityProducesActionableException` | 迁移为 MSTest；参数化行保留 |
| `CorruptIdentityProducesActionableException` | 保留 | `LocalSendDotNet.Core.Tests/IdentityTests.cs` → `CorruptIdentityProducesActionableException` | 迁移为 MSTest；参数化行保留 |
| `ConcurrentFirstLoadPublishesOneStableIdentity` | 保留 | `LocalSendDotNet.Core.Tests/IdentityTests.cs` → `ConcurrentFirstLoadPublishesOneStableIdentity` | 迁移为 MSTest；参数化行保留 |

## tests/LocalSendDotNet.Core.Tests/NetworkAddressPatternsTests.cs

| 原方法 | 处理 | 当前位置/方法 | 说明 |
| --- | --- | --- | --- |
| `NullListsDoNotIgnoreInterfaces` | 保留 | `LocalSendDotNet.Core.Tests/NetworkAddressPatternsTests.cs` → `NullListsDoNotIgnoreInterfaces` | 迁移为 MSTest；参数化行保留 |
| `EmptyWhitelistIgnoresEveryInterface` | 保留 | `LocalSendDotNet.Core.Tests/NetworkAddressPatternsTests.cs` → `EmptyWhitelistIgnoresEveryInterface` | 迁移为 MSTest；参数化行保留 |
| `WhitelistKeepsMatchingInterface` | 保留 | `LocalSendDotNet.Core.Tests/NetworkAddressPatternsTests.cs` → `WhitelistKeepsMatchingInterface` | 迁移为 MSTest；参数化行保留 |
| `BlacklistIgnoresMatchingInterface` | 保留 | `LocalSendDotNet.Core.Tests/NetworkAddressPatternsTests.cs` → `BlacklistIgnoresMatchingInterface` | 迁移为 MSTest；参数化行保留 |
| `BlacklistIgnoresWholeInterfaceWhenAnyAddressMatches` | 保留 | `LocalSendDotNet.Core.Tests/NetworkAddressPatternsTests.cs` → `BlacklistIgnoresWholeInterfaceWhenAnyAddressMatches` | 迁移为 MSTest；参数化行保留 |
| `WhitelistTakesPrecedenceOverBlacklist` | 保留 | `LocalSendDotNet.Core.Tests/NetworkAddressPatternsTests.cs` → `WhitelistTakesPrecedenceOverBlacklist` | 迁移为 MSTest；参数化行保留 |
| `MatchesDottedQuadPatterns` | 保留 | `LocalSendDotNet.Core.Tests/NetworkAddressPatternsTests.cs` → `MatchesDottedQuadPatterns` | 迁移为 MSTest；参数化行保留 |

## tests/LocalSendDotNet.Core.Tests/NodeIntegrationTests.cs

| 原方法 | 处理 | 当前位置/方法 | 说明 |
| --- | --- | --- | --- |
| `TwoNodesTransferTextOverMutualTls` | 保留 | `LocalSendDotNet.Core.Tests/NodeIntegrationTests.cs` → `TwoNodesTransferTextOverMutualTls` | 迁移为 MSTest；参数化行保留 |
| `KnownDeviceCanBeAddedAndWaitingTransferCanBeCancelledById` | 保留 | `LocalSendDotNet.Core.Tests/NodeIntegrationTests.cs` → `KnownDeviceCanBeAddedAndWaitingTransferCanBeCancelledById` | 迁移为 MSTest；参数化行保留 |
| `LargeVirtualStreamTransfersWithoutMaterializingSource` | 保留 | `LocalSendDotNet.Core.Tests/NodeIntegrationTests.cs` → `LargeVirtualStreamTransfersWithoutMaterializingSource` | 迁移为 MSTest；参数化行保留 |
| `PinAndPartialAcceptanceAreEnforced` | 保留 | `LocalSendDotNet.Core.Tests/NodeIntegrationTests.cs` → `PinAndPartialAcceptanceAreEnforced` | 迁移为 MSTest；参数化行保留 |

## tests/LocalSendDotNet.Core.Tests/ProtocolTests.cs

| 原方法 | 处理 | 当前位置/方法 | 说明 |
| --- | --- | --- | --- |
| `AnnouncementUsesV22CamelCaseShape` | 保留 | `LocalSendDotNet.Core.Tests/ProtocolTests.cs` → `AnnouncementUsesV22CamelCaseShape` | 迁移为 MSTest；参数化行保留 |
| `RegisterPayloadOmitsFalseAnnounceExtension` | 保留 | `LocalSendDotNet.Core.Tests/ProtocolTests.cs` → `RegisterPayloadOmitsFalseAnnounceExtension` | 迁移为 MSTest；参数化行保留 |
| `FileSizeIsDeserializedAsInt64` | 保留 | `LocalSendDotNet.Core.Tests/ProtocolTests.cs` → `FileSizeIsDeserializedAsInt64` | 迁移为 MSTest；参数化行保留 |
| `UnknownDeviceTypeFallsBackToDesktop` | 保留 | `LocalSendDotNet.Core.Tests/ProtocolTests.cs` → `UnknownDeviceTypeFallsBackToDesktop` | 迁移为 MSTest；参数化行保留 |
| `InvalidJsonIsRejected` | 保留 | `LocalSendDotNet.Core.Tests/ProtocolTests.cs` → `InvalidJsonIsRejected` | 迁移为 MSTest；参数化行保留 |

## tests/LocalSendDotNet.Core.Tests/PublicApiBaselineTests.cs

| 原方法 | 处理 | 当前位置/方法 | 说明 |
| --- | --- | --- | --- |
| `ExportedApiMatchesApprovedBaseline` | 保留 | `LocalSendDotNet.Core.Tests/PublicApiBaselineTests.cs` → `ExportedApiMatchesApprovedBaseline` | 迁移为 MSTest；参数化行保留 |

## tests/LocalSendDotNet.Core.Tests/ReliabilityTests.cs

| 原方法 | 处理 | 当前位置/方法 | 说明 |
| --- | --- | --- | --- |
| `ReliableBroadcastWaitsInsteadOfDroppingDecisionEvents` | 保留 | `LocalSendDotNet.Core.Tests/ReliabilityTests.cs` → `ReliableBroadcastWaitsInsteadOfDroppingDecisionEvents` | 迁移为 MSTest；参数化行保留 |
| `DeviceStoreExpiresAndPublishesRemovedDevice` | 保留 | `LocalSendDotNet.Core.Tests/ReliabilityTests.cs` → `DeviceStoreExpiresAndPublishesRemovedDevice` | 迁移为 MSTest；参数化行保留 |
| `ManuallyTrustedDeviceDoesNotExpire` | 保留 | `LocalSendDotNet.Core.Tests/ReliabilityTests.cs` → `ManuallyTrustedDeviceDoesNotExpire` | 迁移为 MSTest；参数化行保留 |
| `DirectoryItemsPreserveRelativePathsAndInferContentTypes` | 保留 | `LocalSendDotNet.Core.Tests/ReliabilityTests.cs` → `DirectoryItemsPreserveRelativePathsAndInferContentTypes` | 迁移为 MSTest；参数化行保留 |
| `OptionsRejectUnsafeLimits` | 保留 | `LocalSendDotNet.Core.Tests/ReliabilityTests.cs` → `OptionsRejectUnsafeLimits` | 迁移为 MSTest；参数化行保留 |
| `EndpointNormalizesIpv4MappedAddress` | 保留 | `LocalSendDotNet.Core.Tests/ReliabilityTests.cs` → `EndpointNormalizesIpv4MappedAddress` | 迁移为 MSTest；参数化行保留 |
| `StreamItemSupportsUiOwnedStreams` | 保留 | `LocalSendDotNet.Core.Tests/ReliabilityTests.cs` → `StreamItemSupportsUiOwnedStreams` | 迁移为 MSTest；参数化行保留 |
| `PublicSurfaceKeepsProtocolDtosInternalAndUiEntryPointsStable` | 保留 | `LocalSendDotNet.Core.Tests/ReliabilityTests.cs` → `PublicSurfaceKeepsProtocolDtosInternalAndUiEntryPointsStable` | 迁移为 MSTest；参数化行保留 |
| `DiscoveryReceiverCanRebindAfterInterfaceRefresh` | 保留 | `LocalSendDotNet.Core.Tests/ReliabilityTests.cs` → `DiscoveryReceiverCanRebindAfterInterfaceRefresh` | 迁移为 MSTest；参数化行保留 |
| `OccupiedUdpPortLeavesHttpServerRunning` | 保留 | `LocalSendDotNet.Core.Tests/ReliabilityTests.cs` → `OccupiedUdpPortLeavesHttpServerRunning` | 迁移为 MSTest；参数化行保留 |
| `OccupiedTcpPortProducesActionableException` | 保留 | `LocalSendDotNet.Core.Tests/ReliabilityTests.cs` → `OccupiedTcpPortProducesActionableException` | 迁移为 MSTest；参数化行保留 |
| `CancelledStartupCleansUpAndCanBeRetried` | 保留 | `LocalSendDotNet.Core.Tests/ReliabilityTests.cs` → `CancelledStartupCleansUpAndCanBeRetried` | 迁移为 MSTest；参数化行保留 |

## tests/LocalSendDotNet.Core.Tests/SafeFileTargetTests.cs

| 原方法 | 处理 | 当前位置/方法 | 说明 |
| --- | --- | --- | --- |
| `RejectsUnsafePaths` | 保留 | `LocalSendDotNet.Core.Tests/SafeFileTargetTests.cs` → `RejectsUnsafePaths` | 迁移为 MSTest；参数化行保留 |
| `CreatesUniqueNameForCollision` | 保留 | `LocalSendDotNet.Core.Tests/SafeFileTargetTests.cs` → `CreatesUniqueNameForCollision` | 迁移为 MSTest；参数化行保留 |
| `ReservesUniqueNamesBeforeFilesExist` | 保留 | `LocalSendDotNet.Core.Tests/SafeFileTargetTests.cs` → `ReservesUniqueNamesBeforeFilesExist` | 迁移为 MSTest；参数化行保留 |
| `RejectsLinkedDirectoryThatEscapesRoot` | 保留 | `LocalSendDotNet.Core.Tests/SafeFileTargetTests.cs` → `RejectsLinkedDirectoryThatEscapesRoot` | 迁移为 MSTest；参数化行保留 |

## tests/LocalSendDotNet.Core.Tests/TransferOutcomeTests.cs

| 原方法 | 处理 | 当前位置/方法 | 说明 |
| --- | --- | --- | --- |
| `SendOutcomeCarriesOnlyCaseSpecificData` | 保留 | `LocalSendDotNet.Core.Tests/TransferOutcomeTests.cs` → `SendOutcomeCarriesOnlyCaseSpecificData` | 迁移为 MSTest；参数化行保留 |
| `ReceiveFailureRequiresStructuredFailure` | 保留 | `LocalSendDotNet.Core.Tests/TransferOutcomeTests.cs` → `ReceiveFailureRequiresStructuredFailure` | 迁移为 MSTest；参数化行保留 |

## tests/LocalSendDotNet.Core.Tests/TransportReliabilityTests.cs

| 原方法 | 处理 | 当前位置/方法 | 说明 |
| --- | --- | --- | --- |
| `ReceiverHandlesConcurrentUploadsInOneSession` | 保留 | `LocalSendDotNet.Core.Tests/TransportReliabilityTests.cs` → `ReceiverHandlesConcurrentUploadsInOneSession` | 迁移为 MSTest；参数化行保留 |
| `AcceptedButAbandonedSessionTimesOutAndReleasesSlot` | 保留 | `LocalSendDotNet.Core.Tests/TransportReliabilityTests.cs` → `AcceptedButAbandonedSessionTimesOutAndReleasesSlot` | 迁移为 MSTest；参数化行保留 |
| `BusyReceiverRejectsNewSessionWithoutQueueing` | 保留 | `LocalSendDotNet.Core.Tests/TransportReliabilityTests.cs` → `BusyReceiverRejectsNewSessionWithoutQueueing` | 迁移为 MSTest；参数化行保留 |
| `IncorrectChecksumFailsAndDoesNotPublishFile` | 保留 | `LocalSendDotNet.Core.Tests/TransportReliabilityTests.cs` → `IncorrectChecksumFailsAndDoesNotPublishFile` | 迁移为 MSTest；参数化行保留 |
| `WrongUploadTokenDoesNotConsumeTheValidToken` | 保留 | `LocalSendDotNet.Core.Tests/TransportReliabilityTests.cs` → `WrongUploadTokenDoesNotConsumeTheValidToken` | 迁移为 MSTest；参数化行保留 |
| `InterruptedBodyRemovesTemporaryFile` | 保留 | `LocalSendDotNet.Core.Tests/TransportReliabilityTests.cs` → `InterruptedBodyRemovesTemporaryFile` | 迁移为 MSTest；参数化行保留 |
| `RegisterRejectsFingerprintThatDiffersFromClientCertificate` | 保留 | `LocalSendDotNet.Core.Tests/TransportReliabilityTests.cs` → `RegisterRejectsFingerprintThatDiffersFromClientCertificate` | 迁移为 MSTest；参数化行保留 |
| `PrepareMetadataLimitRemainsEnforcedWhileLargeUploadsAreAllowed` | 保留 | `LocalSendDotNet.Core.Tests/TransportReliabilityTests.cs` → `PrepareMetadataLimitRemainsEnforcedWhileLargeUploadsAreAllowed` | 迁移为 MSTest；参数化行保留 |

## tests/LocalSendDotNet.Core.Tests/WebShareTests.cs

| 原方法 | 处理 | 当前位置/方法 | 说明 |
| --- | --- | --- | --- |
| `RootIsNotFoundUntilWebShareStarts` | 保留 | `LocalSendDotNet.Core.Tests/WebShareTests.cs` → `RootIsNotFoundUntilWebShareStarts` | 迁移为 MSTest；参数化行保留 |
| `AutoAcceptAllowsImmediateDownload` | 保留 | `LocalSendDotNet.Core.Tests/WebShareTests.cs` → `AutoAcceptAllowsImmediateDownload` | 迁移为 MSTest；参数化行保留 |
| `HostCanAcceptAPendingBrowser` | 保留 | `LocalSendDotNet.Core.Tests/WebShareTests.cs` → `HostCanAcceptAPendingBrowser` | 迁移为 MSTest；参数化行保留 |
| `PinIsRequiredWhenConfigured` | 保留 | `LocalSendDotNet.Core.Tests/WebShareTests.cs` → `PinIsRequiredWhenConfigured` | 迁移为 MSTest；参数化行保留 |
| `BrowserUploadUsesTheNormalIncomingTransferFlow` | 保留 | `LocalSendDotNet.Core.Tests/WebShareTests.cs` → `BrowserUploadUsesTheNormalIncomingTransferFlow` | 迁移为 MSTest；参数化行保留 |

## tests/LocalSendDotNet.InteropTests/OfficialClientInteropTests.cs

| 原方法 | 处理 | 当前位置/方法 | 说明 |
| --- | --- | --- | --- |
| `OfficialClientCompatibilityMatrixSeparatesEvidenceFromManualChecks` | 删除 | — | 仅检查文档标题，未测试客户端；保留人工互通文档 |

## tests/Tonarink.App.Settings.Tests/FilePreviewSettingsTests.cs

| 原方法 | 处理 | 当前位置/方法 | 说明 |
| --- | --- | --- | --- |
| `AddingSameToolTwiceKeepsOneCardAndOriginalPath` | 保留 | `Tonarink.App.WinUI.Tests/Settings/FilePreviewSettingsTests.cs` → `AddingSameToolTwiceKeepsOneCardAndOriginalPath` | 迁移为 MSTest；参数化行保留 |
| `EmptyPathCardSurvivesSerializationAndRestart` | 保留 | `Tonarink.App.WinUI.Tests/Settings/FilePreviewSettingsTests.cs` → `EmptyPathCardSurvivesSerializationAndRestart` | 迁移为 MSTest；参数化行保留 |
| `DeletedToolsAreNotAutomaticallyReAddedAfterRestart` | 保留 | `Tonarink.App.WinUI.Tests/Settings/FilePreviewSettingsTests.cs` → `DeletedToolsAreNotAutomaticallyReAddedAfterRestart` | 迁移为 MSTest；参数化行保留 |
| `RemovingActiveToolSelectsRemainingToolWithoutChangingOtherSettings` | 改写 | `Tonarink.App.WinUI.Tests/Settings/FilePreviewSettingsTests.cs` → `RemovingOverrideKeepsSelectedToolAndOtherSettings` | 修正旧需求：工具选择独立于路径覆盖卡片 |
| `StaleSaveCannotRestoreDeletedPath` | 保留 | `Tonarink.App.WinUI.Tests/Settings/FilePreviewSettingsTests.cs` → `StaleSaveCannotRestoreDeletedPath` | 迁移为 MSTest；参数化行保留 |
| `SavingPathUsesCardTypeNotFileNameAndPreservesSelectedTool` | 保留 | `Tonarink.App.WinUI.Tests/Settings/FilePreviewSettingsTests.cs` → `SavingPathUsesCardTypeNotFileNameAndPreservesSelectedTool` | 迁移为 MSTest；参数化行保留 |
| `ValidationOnlyChecksFileExistenceAndDoesNotMutateSettings` | 保留 | `Tonarink.App.WinUI.Tests/Settings/FilePreviewSettingsTests.cs` → `ValidationOnlyChecksFileExistenceAndDoesNotMutateSettings` | 迁移为 MSTest；参数化行保留 |
| `LegacyPathsAndSelectedRunningQuickLookBecomeCards` | 改写 | `Tonarink.App.WinUI.Tests/Settings/FilePreviewSettingsTests.cs` → `MigrationAddsOnlyCustomOverridesNotSelectedTool` | 修正旧需求：工具选择独立于路径覆盖卡片 |
| `LegacySettingsWithNoToolsCanStillDiscoverInstalledOnes` | 改写 | `Tonarink.App.WinUI.Tests/Settings/FilePreviewSettingsTests.cs` → `DetectedToolsDoNotCreateUnnecessaryOverrideCards` | 修正旧需求：工具选择独立于路径覆盖卡片 |
| `MigrationClearsDetectedPathsButPreservesCustomOverrides` | 保留 | `Tonarink.App.WinUI.Tests/Settings/FilePreviewSettingsTests.cs` → `MigrationClearsDetectedPathsButPreservesCustomOverrides` | 迁移为 MSTest；参数化行保留 |
| `ExplicitOverridesMatchingDetectedPathsArePreserved` | 保留 | `Tonarink.App.WinUI.Tests/Settings/FilePreviewSettingsTests.cs` → `ExplicitOverridesMatchingDetectedPathsArePreserved` | 迁移为 MSTest；参数化行保留 |
| `BlankOverrideIsValidAndRestoresAutomaticDetection` | 保留 | `Tonarink.App.WinUI.Tests/Settings/FilePreviewSettingsTests.cs` → `BlankOverrideIsValidAndRestoresAutomaticDetection` | 迁移为 MSTest；参数化行保留 |
| `ValidOverrideIsNormalizedAndTakesPrecedenceOverDetection` | 保留 | `Tonarink.App.WinUI.Tests/Settings/FilePreviewSettingsTests.cs` → `ValidOverrideIsNormalizedAndTakesPrecedenceOverDetection` | 迁移为 MSTest；参数化行保留 |
| `MissingOverrideCannotBeSavedOrSilentlyReplacedByDetection` | 保留 | `Tonarink.App.WinUI.Tests/Settings/FilePreviewSettingsTests.cs` → `MissingOverrideCannotBeSavedOrSilentlyReplacedByDetection` | 迁移为 MSTest；参数化行保留 |
| `StoredProviderListFiltersUnknownTypesAndDuplicates` | 保留 | `Tonarink.App.WinUI.Tests/Settings/FilePreviewSettingsTests.cs` → `StoredProviderListFiltersUnknownTypesAndDuplicates` | 迁移为 MSTest；参数化行保留 |

## tests/Tonarink.App.Settings.Tests/PreviewExpanderStructureTests.cs

| 原方法 | 处理 | 当前位置/方法 | 说明 |
| --- | --- | --- | --- |
| `CompactButtonsSetNativePaddingAndContentAlignment` | 改写 | `Tonarink.App.WinUI.Tests/Components/FilePreviewContentTests.cs` → `Override_EmptyDisabled_DraftDoesNotSave_InvalidFeedback_ValidExplicitSave` | 实际 native Button 内边距、对齐与尺寸，不读取源码 |
| `VerificationFeedbackMatchesCopyKeyframeTimingsAndEasing` | 删除 | — | 按约定不测试关键帧；成功/失败反馈与显式保存由真实控件案例验证 |
| `OverrideInputIsBoundedAndOnlyExplicitValidationSaves` | 改写 | `Tonarink.App.WinUI.Tests/Components/FilePreviewContentTests.cs` → `Override_EmptyDisabled_DraftDoesNotSave_InvalidFeedback_ValidExplicitSave` | 实际输入宽度、失焦、无效反馈和有效保存 |
| `PreviewItemsUseNativeCardFactoryAndOnlyWrapCardContent` | 改写 | `Tonarink.App.WinUI.Tests/Components/FilePreviewContentTests.cs` → `Override_EmptyDisabled_DraftDoesNotSave_InvalidFeedback_ValidExplicitSave` | 实际挂载的根节点为 SettingsCard |

## tests/Tonarink.App.Settings.Tests/SettingsInputDraftTests.cs

| 原方法 | 处理 | 当前位置/方法 | 说明 |
| --- | --- | --- | --- |
| `EditingDoesNotMutatePersistedSettings` | 保留 | `Tonarink.App.WinUI.Tests/Settings/SettingsInputDraftTests.cs` → `EditingDoesNotMutatePersistedSettings` | 迁移为 MSTest；参数化行保留 |
| `ApplyMergesInputsAndPreservesLatestSwitchesAndNetworkLists` | 保留 | `Tonarink.App.WinUI.Tests/Settings/SettingsInputDraftTests.cs` → `ApplyMergesInputsAndPreservesLatestSwitchesAndNetworkLists` | 迁移为 MSTest；参数化行保留 |
| `InvalidPortCannotBeApplied` | 保留 | `Tonarink.App.WinUI.Tests/Settings/SettingsInputDraftTests.cs` → `InvalidPortCannotBeApplied` | 迁移为 MSTest；参数化行保留 |
| `InvalidTimeoutCannotBeApplied` | 保留 | `Tonarink.App.WinUI.Tests/Settings/SettingsInputDraftTests.cs` → `InvalidTimeoutCannotBeApplied` | 迁移为 MSTest；参数化行保留 |
| `InvalidMulticastCannotBeApplied` | 保留 | `Tonarink.App.WinUI.Tests/Settings/SettingsInputDraftTests.cs` → `InvalidMulticastCannotBeApplied` | 迁移为 MSTest；参数化行保留 |
| `EmptyPinIsRejectedOnlyWhenEnabled` | 保留 | `Tonarink.App.WinUI.Tests/Settings/SettingsInputDraftTests.cs` → `EmptyPinIsRejectedOnlyWhenEnabled` | 迁移为 MSTest；参数化行保留 |
| `OversizedPinIsRejected` | 保留 | `Tonarink.App.WinUI.Tests/Settings/SettingsInputDraftTests.cs` → `OversizedPinIsRejected` | 迁移为 MSTest；参数化行保留 |
| `BlankNameModelAndMulticastKeepDefaultResolution` | 保留 | `Tonarink.App.WinUI.Tests/Settings/SettingsInputDraftTests.cs` → `BlankNameModelAndMulticastKeepDefaultResolution` | 迁移为 MSTest；参数化行保留 |
| `RebaseRefreshesUntouchedInputsWithoutLosingDraftsOrEmptyNumbers` | 保留 | `Tonarink.App.WinUI.Tests/Settings/SettingsInputDraftTests.cs` → `RebaseRefreshesUntouchedInputsWithoutLosingDraftsOrEmptyNumbers` | 迁移为 MSTest；参数化行保留 |

## tests/Tonarink.Application.Tests/AppLanguagesTests.cs

| 原方法 | 处理 | 当前位置/方法 | 说明 |
| --- | --- | --- | --- |
| `ResolveMapsIndexAndSystemCulture` | 保留 | `Tonarink.Application.Tests/AppLanguagesTests.cs` → `ResolveMapsIndexAndSystemCulture` | 迁移为 MSTest；参数化行保留 |
| `MatchFallsBackToEnglish` | 保留 | `Tonarink.Application.Tests/AppLanguagesTests.cs` → `MatchFallsBackToEnglish` | 迁移为 MSTest；参数化行保留 |
| `FormatReplacesNamedPlaceholders` | 保留 | `Tonarink.Application.Tests/AppLanguagesTests.cs` → `FormatReplacesNamedPlaceholders` | 迁移为 MSTest；参数化行保留 |

## tests/Tonarink.Application.Tests/TonarinkAppStateTests.cs

| 原方法 | 处理 | 当前位置/方法 | 说明 |
| --- | --- | --- | --- |
| `InitializeRestoresAndNormalizesStoredSettings` | 保留 | `Tonarink.Application.Tests/TonarinkAppStateTests.cs` → `InitializeRestoresAndNormalizesStoredSettings` | 迁移为 MSTest；参数化行保留 |
| `UpdatePersistsNormalizedSettingsAndRaisesChange` | 保留 | `Tonarink.Application.Tests/TonarinkAppStateTests.cs` → `UpdatePersistsNormalizedSettingsAndRaisesChange` | 迁移为 MSTest；参数化行保留 |
| `MissingReceivePinJsonDeserializesAsDisabled` | 保留 | `Tonarink.Application.Tests/TonarinkAppStateTests.cs` → `MissingReceivePinJsonDeserializesAsDisabled` | 迁移为 MSTest；参数化行保留 |
| `RemovingImportedSendItemDeletesTheTempFile` | 保留 | `Tonarink.Application.Tests/TonarinkAppStateTests.cs` → `RemovingImportedSendItemDeletesTheTempFile` | 迁移为 MSTest；参数化行保留 |
| `ClearingSendQueueReleasesImportedFilesAndCleanupKeepsHeldItems` | 保留 | `Tonarink.Application.Tests/TonarinkAppStateTests.cs` → `ClearingSendQueueReleasesImportedFilesAndCleanupKeepsHeldItems` | 迁移为 MSTest；参数化行保留 |
| `FolderItemsUseRelativeProtocolNames` | 保留 | `Tonarink.Application.Tests/TonarinkAppStateTests.cs` → `FolderItemsUseRelativeProtocolNames` | 迁移为 MSTest；参数化行保留 |
| `DeviceAddressParsesIpAndPort` | 保留 | `Tonarink.Application.Tests/TonarinkAppStateTests.cs` → `DeviceAddressParsesIpAndPort` | 迁移为 MSTest；参数化行保留 |

## tests/Tonarink.Blazor.Shared.Tests/UiStructureTests.cs

| 原方法 | 处理 | 当前位置/方法 | 说明 |
| --- | --- | --- | --- |
| `PrimaryNavListsReceiveSendSettingsAndNotHistory` | 改写 | `Tonarink.Blazor.Shared.Tests/Components/PageBehaviorTests.cs` → `NavigationOffersPrimaryRoutesAndRespondsToRequestedRoute` | 实际 desktop/mobile 导航与业务请求路由 |
| `SendExposesFourActionsAndTwoPanesWithSecondaryAddress` | 改写 | `Tonarink.Blazor.Shared.Tests/Components/PageBehaviorTests.cs` → `SendNativePickerCapabilitiesControlActions / SendTextDialogAddsTrimmedUnicodeContentAndCloses` | 实际按钮可用性、文本对话框和队列；两栏视觉布局不声称已经验证 |
| `SettingsExposesAliasThemeLanguageAutoAcceptAndReceivePin` | 改写 | `Tonarink.Blazor.Shared.Tests/Components/PageBehaviorTests.cs` → `SettingsSwitchSavesAndExternalStateChangeUpdatesMountedControl / SettingsRendersInputsAndPersistsThemeAndLanguageSelections` | 实际控件、密码类型、主题/语言和保存 |
| `ReceiveKeepsIncomingReviewAndHistoryIsSecondary` | 改写 | `Tonarink.Blazor.Shared.Tests/Components/PageBehaviorTests.cs` → `ReceiveAddsIncomingLinkAndKeepsHistorySecondary` | 实际新增/移除请求链接、secondary history 入口 |
| `HybridHostIncludesFluentThemeCss` | 改写 | `Tonarink.Blazor.Shared.Tests/Components/PageBehaviorTests.cs` → `HybridHostReferencesShippedFluentStyles` | 保留宿主资产契约，标记 Distribution，不宣称 UI 自动化 |

## tests/Tonarink.LocalSend.Tests/LocalSendRuntimeTests.cs

| 原方法 | 处理 | 当前位置/方法 | 说明 |
| --- | --- | --- | --- |
| `TextAndFileTransferWriteExpectedBytes` | 合并 | `Tonarink.Application.Tests/LocalSendRuntimeTests.cs` → `TextAndFileTransferWriteExpectedBytes` | 保留实际业务收发，原项目移除 |
| `SendWithoutRequiredPinReturnsOutcomeAndSucceedsWithPin` | 合并 | `Tonarink.Application.Tests/LocalSendRuntimeTests.cs` → `SendWithoutRequiredPinReturnsOutcomeAndSucceedsWithPin` | 保留实际业务收发，原项目移除 |
| `PartialAcceptSavesOnlySelectedItems` | 合并 | `Tonarink.Application.Tests/LocalSendRuntimeTests.cs` → `PartialAcceptSavesOnlySelectedItems` | 保留实际业务收发，原项目移除 |
| `AutoAcceptSavesWithoutUiDecision` | 合并 | `Tonarink.Application.Tests/LocalSendRuntimeTests.cs` → `AutoAcceptSavesWithoutUiDecision` | 保留实际业务收发，原项目移除 |
| `AliasAndReceivePinChangesApplyAfterRestart` | 合并 | `Tonarink.Application.Tests/LocalSendRuntimeTests.cs` → `AliasAndReceivePinChangesApplyAfterRestart` | 保留实际业务收发，原项目移除 |
