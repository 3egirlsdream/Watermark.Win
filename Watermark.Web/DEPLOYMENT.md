# Watermark.Web 自动发布

`.github/workflows/deploy-markweb.yml` 会在 `master` 分支中的 Web、Razor 或 Shared 代码发生变化后自动执行：

1. 安装并锁定当前最新的 .NET 8 SDK，还原并发布 `Watermark.Web`。
2. 通过 SSH/rsync 将发布结果同步到服务器 `/data/markweb/published`。
3. 在服务器用 `Watermark.Web/deploy/Dockerfile` 构建新镜像。
4. 新镜像构建成功后才替换 `markweb` 容器。
5. 请求 `http://127.0.0.1/` 做健康检查；失败时恢复上一镜像和上一份发布文件。

工作流也支持在 GitHub Actions 页面通过 `Run workflow` 手动执行。

## GitHub 配置

在仓库的 **Settings → Secrets and variables → Actions** 中添加以下 Repository secrets：

| 名称 | 内容 |
| --- | --- |
| `DEPLOY_HOST` | 服务器域名或 IP，不带 `http://` |
| `DEPLOY_USER` | 有权写入部署目录并执行 Docker 的 SSH 用户 |
| `DEPLOY_SSH_KEY` | 部署专用 SSH 私钥的完整内容 |
| `DEPLOY_KNOWN_HOSTS` | 服务器的 SSH host key，建议从可信机器执行 `ssh-keyscan -H -p 22 服务器地址` 获取 |

可选的 Repository variables：

| 名称 | 默认值 | 用途 |
| --- | --- | --- |
| `DEPLOY_PORT` | `22` | SSH 端口 |
| `DEPLOY_PATH` | `/data/markweb` | 服务器部署目录 |

## 服务器一次性准备

以下命令中的 `deploy` 请替换为 `DEPLOY_USER` 对应的用户名：

```bash
sudo mkdir -p /data/markweb
sudo chown -R deploy:deploy /data/markweb
sudo usermod -aG docker deploy
```

重新登录 SSH，使 Docker 用户组权限生效，并确认这些命令无需 `sudo`：

```bash
docker version
docker ps
curl --version
```

服务器端不需要安装 .NET SDK，只需要 Docker、SSH、`bash` 和 `curl`。端口 `80` 仍映射到容器的 `8080`，容器名和镜像名保持为 `markweb`。

## 创建部署专用密钥

建议不要复用个人 SSH 私钥：

```bash
ssh-keygen -t ed25519 -C "github-actions-markweb" -f ~/.ssh/markweb_actions
ssh-copy-id -i ~/.ssh/markweb_actions.pub deploy@服务器地址
```

把 `~/.ssh/markweb_actions` 的内容保存到 `DEPLOY_SSH_KEY`。私钥只放在 GitHub Secret 中，不要提交到仓库。

## 首次验证

1. 先在 Actions 页面手动运行一次 `Deploy Watermark.Web`。
2. 成功后执行 `docker ps --filter name=markweb`，应看到 `0.0.0.0:80->8080/tcp`。
3. 打开网站确认资源和 Blazor 交互正常。
4. 后续向 `master` 提交相关代码便会自动发布。
