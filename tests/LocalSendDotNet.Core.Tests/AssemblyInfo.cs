using Microsoft.VisualStudio.TestTools.UnitTesting;

// Real network fixtures bind ports and modify process-wide resources. Keep this
// suite serial until each fixture's parallel ownership has been audited.
[assembly: DoNotParallelize]
