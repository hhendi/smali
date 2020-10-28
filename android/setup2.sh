#!/bin/sh
unset LD_PRELOAD
apt update -y
apt install -y libssl-dev openjdk-14-jre-headless adb
wget -q https://gitlab.com/JFronny/smalipatcher/-/jobs/artifacts/master/download?job=android -O tmp.zip
unzip tmp.zip
rm tmp.zip
chmod a+x smp/SmaliPatcherMin
git clone https://github.com/anestisb/vdexExtractor vdx
cd vdx
./make.sh
cp bin/vdexExtractor ..
cd ..
rm -rf vdx
wget -q https://gitlab.com/JFronny/smalipatcher/-/raw/master/android/cp_framework.sh?inline=false -O cp_framework.sh
chmod a+x cp_framework.sh
wget -q https://gitlab.com/JFronny/smalipatcher/-/raw/master/android/smalipatcher.sh?inline=false -O smalipatcher.sh
chmod a+x smalipatcher.sh