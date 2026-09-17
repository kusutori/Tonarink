# Development MSIX installation

GitHub Releases ship a sideload ZIP for each architecture
(`Tonarink-<version>-x64-msix.zip` or `...-ARM64-msix.zip`).
The ZIP contains `Install.ps1`, the `.msix`, and the matching certificate.

1. Download the ZIP that matches the computer: `x64` for Intel/AMD PCs, `ARM64`
   for Windows on Arm. The standard `-msix.zip` build is recommended for most
   users; `-aot-msix.zip` installs the Native AOT variant, while
   `-portable.zip` runs without installation.
2. Extract the archive.
3. In the extracted `*_Test` folder, run `Install.ps1`. The script installs
   the certificate if needed and then installs the package.

Only install packages downloaded directly from the
`kusutori/Tonarink` GitHub repository. Remove the development certificate
from the Local Machine **Trusted People** store when these builds are no longer
needed.

Future Microsoft Store packages use Microsoft's Store signature and do not
require this sideload script.
