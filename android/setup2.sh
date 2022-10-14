#!/bin/sh
unset LD_PRELOAD
apt update -y
apt install -y libssl-dev openjdk-14-jre-headless figlet zlib1g-dev
figlet Installing dotnet
wget -q https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod a+x dotnet-install.sh
./dotnet-install.sh -c 5.0 #TODO change if updated
figlet Fetching patcher
if [ "$(uname -m)" = "aarch64" ]
then
    wget -q https://github.com/hhendi/smali/tree/dev/SmaliPatcher/-/jobs/artifacts/master/download?job=android -O tmp.zip
elif [ "$(uname -m)" = "arm64" ]
then
    wget -q https://github.com/hhendi/smali/tree/dev/SmaliPatcher/-/jobs/artifacts/master/download?job=android -O tmp.zip
elif [ "$(uname -m)" = "armv7l" ]
then
    wget -q https://github.com/hhendi/smali/tree/dev/SmaliPatcher/-/jobs/artifacts/master/download?job=android-arm -O tmp.zip
elif [ "$(uname -m)" = "arm" ]
then
    wget -q https://github.com/hhendi/smali/tree/dev/SmaliPatcher/-/jobs/artifacts/master/download?job=android-arm -O tmp.zip
fi
unzip tmp.zip
rm tmp.zip
figlet Building vdexExtractor
git clone https://github.com/hhendi/vdex vdx
cd vdx
./make.sh
cp bin/vdexExtractor ..
cd ..
rm -rf vdx
figlet downgrade openssl
wget http://ports.ubuntu.com/ubuntu-ports/pool/main/o/openssl/libssl1.1_1.1.1f-1ubuntu2.16_arm64.deb
wget http://ports.ubuntu.com/ubuntu-ports/pool/main/o/openssl/openssl_1.1.1f-1ubuntu2.16_arm64.deb
wget http://ports.ubuntu.com/ubuntu-ports/pool/main/o/openssl/libssl-dev_1.1.1f-1ubuntu2.16_arm64.deb
dpkg -i libssl1.1_1.1.1f-1ubuntu2.16_arm64.deb
dpkg -i libssl-dev_1.1.1f-1ubuntu2.16_arm64.deb
dpkg -i openssl_1.1.1f-1ubuntu2.16_arm64.deb
echo Fetching scripts
wget -q https://github.com/hhendi/smali/blob/dev/android/cp_framework.sh?inline=false -O cp_framework.sh
chmod a+x cp_framework.sh
wget -q https://github.com/hhendi/smali/blob/dev/android/smalipatcher.sh?inline=false -O smalipatcher.sh
chmod a+x smalipatcher.sh
echo Env setup complete
