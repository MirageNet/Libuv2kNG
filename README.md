
[![Documentation](https://img.shields.io/badge/documentation-brightgreen.svg)](https://miragenet.github.io/Mirage/)
[![Forum](https://img.shields.io/badge/forum-brightgreen.svg)](https://forum.unity.com/threads/mirror-networking-for-unity-aka-hlapi-community-edition.425437/)
[![Discord](https://img.shields.io/discord/343440455738064897.svg)]()
[![release](https://img.shields.io/github/release/MirageNet/Libuv2kNG.svg)](https://github.com/MirageNet/Libuv2kNG/releases/latest)
[![openupm](https://img.shields.io/npm/v/com.miragenet.libuv2k?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.miragenet.libuv2k/)
[![GitHub issues](https://img.shields.io/github/issues/MirageNet/Libuv2kNG.svg)](https://github.com/MirageNet/Libuv2kNG/issues)
![GitHub last commit](https://img.shields.io/github/last-commit/MirageNet/Libuv2kNG.svg) ![MIT Licensed](https://img.shields.io/badge/license-MIT-green.svg)
[![Build](https://github.com/MirageNet/Libuv2kNG/workflows/CI/badge.svg)](https://github.com/MirageNet/Libuv2kNG/actions?query=workflow%3ACI)

# Libuv2kNG

This is a port of vis2k libuv2k for mirage. You still need to be a subscriber to get the files from vis2k. This transport code does not work without the rest of the files from mirage subscription.

Remove or rename the #IFDEF for Mirage in LibuvUtils.cs and rename namespace to mirrage

## Installation
The preferred installation method is Unity Package manager.

If you are using unity 2019.3 or later: 

1) Open your project in unity
2) Install [Mirage](https://github.com/MirageNet/Mirage)
3) Click on Windows -> Package Manager
4) Click on the plus sign on the left and click on "Add package from git URL..."
5) enter https://github.com/MirageNet/Libuv2kNG.git?path=/Assets/Mirage/Runtime/Transport/Libuv2kNG
6) Unity will download and install Mirage SteamyNG
