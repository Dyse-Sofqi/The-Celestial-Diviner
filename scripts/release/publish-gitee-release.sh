#!/usr/bin/env bash
# 在 Gitee 创建 Release 并上传安装包（需 Gitee 私人令牌，勾选 projects 权限）。
# 用法：GITEE_TOKEN=你的令牌 bash scripts/release/publish-gitee-release.sh <tag> <zip> <notes.md>
# 示例：GITEE_TOKEN=xxx bash scripts/release/publish-gitee-release.sh v1.7.4 \
#         dist/TheCelestialDiviner-1.7.4-win64.zip docs/release-notes/v1.7.4-release-notes.md
set -euo pipefail
: "${GITEE_TOKEN:?请先设置 GITEE_TOKEN（Gitee 私人令牌，需 projects 权限）}"
TAG="${1:?用法: <tag> <zip> <notes.md>}"
ZIP="${2:?缺少 zip 路径}"
NOTES="${3:?缺少发布说明路径}"
REPO="sofqi/The-Celestial-Diviner"

echo "1/2 创建 Release $TAG ..."
resp=$(curl -sS -m 60 -X POST "https://gitee.com/api/v5/repos/$REPO/releases" \
  --data-urlencode "access_token=$GITEE_TOKEN" \
  --data-urlencode "tag_name=$TAG" \
  --data-urlencode "name=$TAG" \
  --data-urlencode "target_commitish=main" \
  --data-urlencode "prerelease=false" \
  --data-urlencode "body@$NOTES")
id=$(printf '%s' "$resp" | python -c "import sys,json;print(json.load(sys.stdin).get('id',''))" 2>/dev/null || true)
if [ -z "$id" ]; then echo "创建失败：$resp"; exit 1; fi
echo "  release id = $id"

echo "2/2 上传附件 $ZIP ..."
curl -sS -m 300 -X POST "https://gitee.com/api/v5/repos/$REPO/releases/$id/attach_files" \
  -F "access_token=$GITEE_TOKEN" \
  -F "file=@$ZIP" | head -c 300
echo
echo "完成：https://gitee.com/$REPO/releases/tag/$TAG"
