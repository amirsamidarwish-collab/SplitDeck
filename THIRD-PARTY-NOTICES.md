# Third-party notices

## Virtual Display Driver

The unmodified driver in `assets/driver` is from [VirtualDrivers/Virtual-Display-Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver), release **25.7.23**, asset `VirtualDisplayDriver-x86.Driver.Only.zip`. The INF targets **NTamd64** despite the asset's name. Driver version in the INF is **11.30.4.434**, dated 2024-12-24. Copyright (c) 2024 Virtual Display; MIT license reproduced in `assets/driver/LICENSE.txt`.

The DLL and catalog were verified with Windows Authenticode as **Valid**, signed by **SignPath Foundation**, on the development machine. Runtime setup checks file hashes and Windows signature trust before staging the package. Windows remains responsible for accepting or rejecting driver installation.

| File | SHA-256 |
| --- | --- |
| MttVDD.inf | 550D211FE481E74DFE3F9D724ED78BE48B3A9113405965D683D9373E8D672F5D |
| MttVDD.dll | C9CA837F57A98FBD43BC416A7F535A95843626E7759EAF85CF0CD7CE334DBB05 |
| mttvdd.cat | 08A0093FC9B2E32B287A6F8A77CA4DE0A31830D29FC33D2B13A918DC859468F6 |

Release archive SHA-256: `E24210692B442B39AF763536330CE78B423F19342B7A7792C26DE3944E418B3A`.

## Managed dependencies

- Vortice.Direct3D11 / Vortice.D3DCompiler **3.8.3** and transitive Vortice/SharpGen dependencies: [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows), MIT. NuGet packages retain their upstream license metadata.
- System.Management **10.0.0** and .NET runtime: Microsoft .NET, MIT and third-party notices supplied by the self-contained runtime.

SplitDeck itself is distributed under the MIT license in `LICENSE`. SplitDeck is not affiliated with LG, Microsoft, NVIDIA, Discord, or the upstream driver authors.

The MIT license below applies to the Vortice and SharpGen components with these notices:

- Copyright (c) Amer Koleci and Contributors (Vortice.Windows).
- Copyright (c) 2010-2017 Alexandre Mutel, 2017-2023 Jeremy Koritzinsky, 2023-2024 Amer Koleci (SharpGen.Runtime).

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
