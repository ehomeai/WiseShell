# Third-party components

WiseShell C++ application code is covered by the repository MIT license.

| Component | Locked version | License |
| --- | --- | --- |
| Qt base | 6.8.3 release SDK (minimum 6.2) | LGPL-3.0 / GPL / commercial; dynamically linked |
| libssh | 0.11.5, a09fdd00416e53b6ed436df6ff14339a4f884601 | LGPL-2.1-or-later; dynamically linked |
| QtKeychain | 0.15.0, ad7344c45a86a4f66cbafc4b081b5f7b876cb0b7 | Modified BSD; dynamically linked |
| libvterm | 0.3.3, 9d6d2112335080312ef8c36667fa717ded4f7daf | MIT; statically linked |
| OpenSSL | Platform build dependency | Apache-2.0 for OpenSSL 3 |
| zlib | Platform build dependency | zlib |

Sources: https://code.qt.io/cgit/qt/qtbase.git/ ; https://github.com/libssh/libssh-mirror ; https://github.com/frankosterfeld/qtkeychain ; https://github.com/neovim/libvterm ; https://openssl.org/ ; https://zlib.net/ .

Distribution scripts must include the dependency license texts and shared libraries. Users can replace the dynamically linked LGPL libraries with compatible builds. This project does not add restrictions on reverse engineering those libraries for debugging modifications. No Qt WebEngine or WebView runtime is used.
