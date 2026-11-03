#!/usr/bin/env bash
# 编译原生隐私同意启动页 → Assets/Plugins/Android/privacy-consent.jar
#
# 为什么不用 AAR / Android Studio：
# PrivacyActivity 只依赖 Android framework，不引用 Unity 类（UnityPlayerActivity 用类名拉起），
# 所以用 Unity 自带的 javac + android.jar 编成 JAR 即可，零 Gradle 依赖、可复现。
#
# 用法：tools/build_privacy_activity.sh
# 换 Unity 版本或安装路径时用 UNITY_ANDROID_ROOT 覆盖。
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY_ANDROID="${UNITY_ANDROID_ROOT:-/Applications/Unity/Hub/Editor/6000.6.7f1/PlaybackEngines/AndroidPlayer}"

JAVAC="$UNITY_ANDROID/OpenJDK/bin/javac"
JAR="$UNITY_ANDROID/OpenJDK/bin/jar"
ANDROID_JAR="$UNITY_ANDROID/SDK/platforms/android-34/android.jar"

SRC="$ROOT/tools/android-privacy/src/com/p1studio/inkturfdemo/PrivacyActivity.java"
DEST="$ROOT/Assets/Plugins/Android/privacy-consent.jar"
CS_FILE="$ROOT/Assets/_Project/Scripts/UI/PrivacyConsentDialog.cs"

for f in "$JAVAC" "$JAR" "$ANDROID_JAR" "$SRC" "$CS_FILE"; do
    if [ ! -f "$f" ]; then
        echo "[privacy] 缺少文件: $f" >&2
        exit 1
    fi
done

# 政策版本号必须与 C# 侧一致，否则原生同意后 Unity 会再次弹窗。
CS_VERSION="$(sed -n 's/.*PolicyVersion = "\([^"]*\)".*/\1/p' "$CS_FILE" | head -1)"
JAVA_VERSION="$(sed -n 's/.*POLICY_VERSION = "\([^"]*\)".*/\1/p' "$SRC" | head -1)"
if [ -z "$CS_VERSION" ] || [ "$CS_VERSION" != "$JAVA_VERSION" ]; then
    echo "[privacy] 政策版本不一致：C#='$CS_VERSION' Java='$JAVA_VERSION'" >&2
    exit 1
fi

TMP="$(mktemp -d "${TMPDIR:-/tmp}/p1_privacy.XXXXXX")"
trap 'rm -rf "$TMP"' EXIT
mkdir -p "$TMP/classes" "$(dirname "$DEST")"

"$JAVAC" -encoding UTF-8 -source 8 -target 8 -Xlint:-options \
    -bootclasspath "$ANDROID_JAR" \
    -d "$TMP/classes" "$SRC"

"$JAR" cf "$DEST" -C "$TMP/classes" .

if ! "$JAR" tf "$DEST" | grep -q "com/p1studio/inkturfdemo/PrivacyActivity.class"; then
    echo "[privacy] 产物校验失败：JAR 里没有 PrivacyActivity.class" >&2
    exit 1
fi

echo "[privacy] 已生成 ${DEST}（政策版本 ${JAVA_VERSION}，$(wc -c < "$DEST") 字节）"
