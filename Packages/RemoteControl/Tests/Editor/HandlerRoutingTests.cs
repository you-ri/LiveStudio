using NUnit.Framework;
using Lilium.RemoteControl.RestApi;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// 内側ディスパッチ(<c>DispatchEndpoints</c>)の一致判定 <c>MatchPattern</c> と、
    /// 旧 if/else 連鎖の評価順を宣言表で再現するためのルート順序不変条件を検証する。
    /// HTTP ハンドラを直接叩く自動テストが無いため、ルーティングの中核ロジックを
    /// ここで単体検証する。
    /// </summary>
    [TestFixture]
    public class HandlerRoutingTests
    {
        // MatchPattern は protected static、RouteMatch は protected enum。
        // 派生クラスからのみアクセスできるため、enum をシグネチャに出さない
        // 公開ラッパで露出する。
        private sealed class Probe : BaseRemoteControlApiHandler
        {
            public Probe() : base(null) { }
            public override void Cleanup() { }

            public static bool Exact(string path, string pattern)
                => MatchPattern(path, pattern, RouteMatch.Exact);
            public static bool Prefix(string path, string pattern)
                => MatchPattern(path, pattern, RouteMatch.Prefix);
            public static bool Wildcard(string path, string pattern)
                => MatchPattern(path, pattern, RouteMatch.Wildcard);
            public static bool ExactOrQuery(string path, string pattern)
                => MatchPattern(path, pattern, RouteMatch.ExactOrQuery);
        }

        [Test]
        public void ExactOrQuery_AcceptsATrailingQueryAndNothingElse()
        {
            // まとめ送りのサブリクエストはパスとクエリを 1 本の文字列で運ぶ。単発 (HTTP) と
            // 同じ 1 つの宣言で両方から引けるようにするための一致方法。
            Assert.IsTrue(Probe.ExactOrQuery("/live/changes", "/live/changes"));
            Assert.IsTrue(Probe.ExactOrQuery("/live/changes?since=5", "/live/changes"));
            Assert.IsTrue(Probe.ExactOrQuery("/LIVE/Changes?since=5", "/live/changes"));

            // 「クエリが続いてもよい」だけで、Prefix ではない。
            Assert.IsFalse(Probe.ExactOrQuery("/live/changes/1", "/live/changes"));
            Assert.IsFalse(Probe.ExactOrQuery("/live/changesx", "/live/changes"));
            Assert.IsFalse(Probe.ExactOrQuery("/live/change", "/live/changes"));
        }

        [Test]
        public void Exact_IsCaseInsensitiveEquality()
        {
            Assert.IsTrue(Probe.Exact("/live/objects", "/live/objects"));
            Assert.IsTrue(Probe.Exact("/LIVE/Objects", "/live/objects"));
            Assert.IsFalse(Probe.Exact("/live/object", "/live/objects"));
            Assert.IsFalse(Probe.Exact("/live/objects/1", "/live/objects"));
        }

        [Test]
        public void Prefix_IsCaseInsensitivePrefix()
        {
            Assert.IsTrue(Probe.Prefix("/live/object/123", "/live/object/"));
            Assert.IsTrue(Probe.Prefix("/LIVE/OBJECT/123", "/live/object/"));
            Assert.IsFalse(Probe.Prefix("/live/objects", "/live/object/"));
        }

        [Test]
        public void Wildcard_AsteriskSpansSlashes()
        {
            Assert.IsTrue(Probe.Wildcard("/live/object/123/foo", "/live/object/*/*"));
            Assert.IsTrue(Probe.Wildcard("/LIVE/object/123/foo", "/live/object/*/*"));
        }

        // --- ルート順序不変条件 (旧 if/else 連鎖の評価順を表で再現できる根拠) ---

        [Test]
        public void Get_ObjectsExact_DoesNotCollideWithObjectWildcardOrPrefix()
        {
            // "/live/objects" は Exact 専用。object/*/* にも object/ Prefix にも一致しない。
            Assert.IsTrue(Probe.Exact("/live/objects", "/live/objects"));
            Assert.IsFalse(Probe.Wildcard("/live/objects", "/live/object/*/*"));
            Assert.IsFalse(Probe.Prefix("/live/objects", "/live/object/"));
        }

        [Test]
        public void Get_SingleObject_FallsToPrefixNotPropertyWildcard()
        {
            // プロパティ無し /live/object/{id} は object/*/* に不一致 → Prefix で GetObject。
            Assert.IsFalse(Probe.Wildcard("/live/object/123", "/live/object/*/*"));
            Assert.IsTrue(Probe.Prefix("/live/object/123", "/live/object/"));
            // プロパティ付きは Wildcard 一致 → GetProperty。
            Assert.IsTrue(Probe.Wildcard("/live/object/123/foo", "/live/object/*/*"));
        }

        [Test]
        public void Post_ResetMustBeEvaluatedBeforeAdd()
        {
            // /@reset 付きは reset ルートに一致し、かつ add の object/*/* にも一致するため
            // テーブル順で reset を先に置く必要がある(本テストはその前提を固定)。
            Assert.IsTrue(Probe.Wildcard("/live/object/1/prop/@reset", "/live/object/*/*/@reset"));
            Assert.IsTrue(Probe.Wildcard("/live/object/1/prop/@reset", "/live/object/*/*"));
            // /@reset 無しは reset ルートに不一致 → add へ。
            Assert.IsFalse(Probe.Wildcard("/live/object/1/prop", "/live/object/*/*/@reset"));
            Assert.IsTrue(Probe.Wildcard("/live/object/1/prop", "/live/object/*/*"));

            // 疑似メンバーが @ で始まる理由: "reset" という名前のメンバーが実在しても、
            // そのメンバーへの append と reset 要求が別物として読める。
            Assert.IsFalse(Probe.Wildcard("/live/object/1/prop/reset", "/live/object/*/*/@reset"));
            Assert.IsTrue(Probe.Wildcard("/live/object/1/prop/reset", "/live/object/*/*"));
        }

        [Test]
        public void Put_ParentMustBeEvaluatedBeforeSetProperty()
        {
            // @parent は専用ルートと汎用 object/*/* の両方に一致するため
            // テーブル順で @parent を先に置く必要がある。
            Assert.IsTrue(Probe.Wildcard("/live/object/1/@parent", "/live/object/*/@parent"));
            Assert.IsTrue(Probe.Wildcard("/live/object/1/@parent", "/live/object/*/*"));
        }

        [Test]
        public void Get_SingularTypeRoutes_DoNotCollideWithPluralOnes()
        {
            // 単数形は Prefix、複数形は Exact。綴りが 1 文字違いなので、
            // どちらかがもう一方を吸わないことを固定する。
            Assert.IsTrue(Probe.Prefix("/live/type/Camera", "/live/type/"));
            Assert.IsFalse(Probe.Prefix("/live/types", "/live/type/"));
            Assert.IsFalse(Probe.Exact("/live/type/Camera", "/live/types"));

            Assert.IsTrue(Probe.Prefix("/live/enum/CameraProjection", "/live/enum/"));
            Assert.IsFalse(Probe.Prefix("/live/enums", "/live/enum/"));
            Assert.IsFalse(Probe.Exact("/live/enum/CameraProjection", "/live/enums"));

            // 型名を持たない裸の /live/type は単数形ルートに載らない
            // (/live/object と同じで、ハンドラ以前に 404)。
            Assert.IsFalse(Probe.Prefix("/live/type", "/live/type/"));
        }

        [Test]
        public void Get_AssetKey_TakesTheWholeTailAndImageIsToldApartBySuffix()
        {
            // キーは URL の末尾を丸ごと占める。中の "/" も先頭の "/" も (エンジンのアセットパス)
            // プレフィックス一致で拾える。
            Assert.IsTrue(Probe.Prefix("/live/asset/0123456789abcdef", "/live/asset/"));
            Assert.IsTrue(Probe.Prefix("/live/asset/file:C:/a/chair.prop.lsb", "/live/asset/"));
            Assert.IsTrue(Probe.Prefix("/live/asset//Game/Props/Chair.Chair", "/live/asset/"));

            // 絵は末尾の疑似メンバーで区別する。キーが何セグメントでも一致すること。
            Assert.IsTrue(Probe.Wildcard("/live/asset/0123456789abcdef/@image", "/live/asset/*/@image"));
            Assert.IsTrue(Probe.Wildcard("/live/asset/file:C:/a/chair.prop.lsb/@image", "/live/asset/*/@image"));
            Assert.IsTrue(Probe.Wildcard("/live/asset//Game/Props/Chair.Chair/@image", "/live/asset/*/@image"));

            // 絵でないキーは @image ルートに一致しない。
            Assert.IsFalse(Probe.Wildcard("/live/asset/file:C:/a/chair.prop.lsb", "/live/asset/*/@image"));

            // ⚠ 絵のパスは単数形の Prefix にも一致する。だから登録順で @image を先に置く必要がある
            // (reset を append より先に置くのと同じ理由)。この前提が崩れると絵が名前で返る。
            Assert.IsTrue(Probe.Prefix("/live/asset/0123456789abcdef/@image", "/live/asset/"));

            // 複数形は単数形の Prefix に吸われない ("/live/assets" は "/live/asset/" で始まらない)。
            Assert.IsFalse(Probe.Prefix("/live/assets", "/live/asset/"));
            Assert.IsTrue(Probe.Exact("/live/assets", "/live/assets"));
        }

        [Test]
        public void Scene_ExportImport_AreExactAndDistinct()
        {
            Assert.IsTrue(Probe.Exact("/live/scene/export", "/live/scene/export"));
            Assert.IsTrue(Probe.Exact("/live/scene/import", "/live/scene/import"));
            Assert.IsFalse(Probe.Exact("/live/scene/export", "/live/scene/import"));
        }
    }
}
