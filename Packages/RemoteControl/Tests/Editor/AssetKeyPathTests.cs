// Copyright (c) You-Ri, 2026
using NUnit.Framework;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// <c>/live/asset/{key}</c> のキー復元。キーは URL の末尾を丸ごと占め、しかも中身は
    /// アセットのファイルパスや保存場所そのものなので、送られた文字列と復元した文字列が
    /// 一致しないと単に「見つからない」になる。
    ///
    /// 入力は**エスケープされたままの**リクエストパス (<c>RawUrl</c> 相当)。
    /// <c>Url.AbsolutePath</c> を渡してはいけない — .NET の Uri 正規化が
    /// <c>%23</c> 以降を切り捨て、連続した <c>/</c> を潰すため、そもそも別のキーになっている
    /// (HttpListener で実測)。
    /// </summary>
    [TestFixture]
    public class AssetKeyPathTests
    {
        [Test]
        public void PlainKey_IsTheWholeTail()
        {
            Assert.AreEqual("0123456789abcdef",
                AssetHandler.ParseKey("/live/asset/0123456789abcdef"));
        }

        [Test]
        public void SlashesInsideKey_AreKept()
        {
            Assert.AreEqual("file:C:/Work/props/chair.prop.lsb",
                AssetHandler.ParseKey("/live/asset/file:C:/Work/props/chair.prop.lsb"));
        }

        [Test]
        public void LeadingSlashKey_SurvivesAsEmptySegment()
        {
            // UE のキー (FSoftObjectPath) は "/" で始まる。URL 上は "//" になるが、
            // 先頭の "/" ごとキーとして戻せないと別のアセットを指してしまう。
            Assert.AreEqual("/Game/Props/Chair.Chair",
                AssetHandler.ParseKey("/live/asset//Game/Props/Chair.Chair"));
        }

        [Test]
        public void EscapedHash_ComesBackAsHash()
        {
            // 外部クリップ参照 (file:<path>#<clip>)。'#' は生では送れないのでクライアントが
            // %23 にする。復元できないとクリップ名が落ちる。
            Assert.AreEqual("file:C:/a/clips.lsb#Walk",
                AssetHandler.ParseKey("/live/asset/file:C:/a/clips.lsb%23Walk"));
        }

        [Test]
        public void LiteralPercent_IsNotConfusedWithAnEscape()
        {
            Assert.AreEqual("file:C:/a/%23.lsb",
                AssetHandler.ParseKey("/live/asset/file:C:/a/%2523.lsb"));
        }

        [Test]
        public void NonAsciiKey_IsUnescaped()
        {
            Assert.AreEqual("file:C:/素材/chair.lsb",
                AssetHandler.ParseKey("/live/asset/file:C:/%E7%B4%A0%E6%9D%90/chair.lsb"));
        }

        [Test]
        public void ImageRoute_KeepsThePseudoMemberInTheTail()
        {
            // 絵のハンドラは末尾の @image を自分で落とす。ここでは尾部として残ることを固定する。
            Assert.AreEqual("/Game/Props/Chair.Chair" + AssetHandler.kImageSuffix,
                AssetHandler.ParseKey("/live/asset//Game/Props/Chair.Chair" + AssetHandler.kImageSuffix));
        }

        [Test]
        public void MissingKey_IsNull()
        {
            Assert.IsNull(AssetHandler.ParseKey("/live/asset/"));
        }
    }
}
