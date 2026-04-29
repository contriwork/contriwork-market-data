# ContriWork çoklu-paket release rehberi (registry bootstrap addendum)

ContriWork ekosistemindeki her paket aynı port + adapter desenini takip ediyor ve aynı tag-push tetikli release pipeline'ından çıkıyor (PyPI + NuGet + npm, all-or-nothing). İlk release'te her registry için bir **bootstrap** adımı zorunlu — bu olmadan `git push origin v0.1.0` sırasında 3 workflow'un üçü de fail eder. Bu doküman, yeni bir paket çıkarırken sıfırdan sona ne yapacağını anlatır.

**Tek-cümle özet**: PyPI ve NuGet için registry-UI'da her yeni paket için ayrı bir trust kaydı, npm için ya trusted publisher ya da `NPM_TOKEN` secret + workflow'da `NODE_AUTH_TOKEN`. Hepsi yapılmadan tag atma.

`TEMPLATE_USAGE.md` §6–8 form alanlarını birebir tablolar ve adımlar halinde verir. Bu doküman ise `gh` CLI ile bootstrap akışını, paket-başına tekrarlanan kararları ve sahaya inmiş gerçek hata mesajlarını yakalar.

---

## 0. Repo-level GitHub Actions tanımları

Her şey `gh` CLI ile, UI gerekmez:

```bash
# NuGet hesap adı (case-sensitive — NuGet account display name'inle birebir).
# Workflow'da `user: ${{ vars.NUGET_ACCOUNT }}` yerine geçer.
gh variable set NUGET_ACCOUNT --repo <github-org>/<github-repo> --body "<NuGetAccountName>"

# npm token — pending trusted publisher rollout'u tamamlanana kadar fallback.
# DİKKAT: npm "Automation" tokenları 90 gün ömürlü. Süre dolduğunda
# npmjs.com → Account → Access Tokens üzerinden yeni token üret ve aşağıdaki
# komutu yeniden çalıştır. Token rotasyonunu takvime / Notion'a yaz.
gh secret set NPM_TOKEN --repo <github-org>/<github-repo>
# Komut prompt açar, npm token'ı yapıştırırsın; chat/log'a düşmez.
```

> PyPI için secret/variable gerekmez — pending publisher tamamen OIDC ile çalışır.

---

## 1. PyPI pending publisher (UI, ~1 dk)

<https://pypi.org/manage/account/publishing/> → **Add a new pending publisher**

| Field | Değer |
|---|---|
| PyPI Project Name | `<pypi-package-name>` (`pyproject.toml` `[project].name` ile birebir) |
| Owner | `<github-org>` |
| Repository name | `<github-repo>` |
| Workflow filename | `release-python.yml` |
| Environment name | `pypi` |

Kayıt edince listede "Pending" görünür; ilk başarılı publish'te otomatik "Active"a döner ve project gerçekten yaratılır.

**Yaygın hata**:

```
400 Non-user identities cannot create new projects.
```

→ Pending publisher tanımı yok ya da proje adı / repo / workflow / env combosu birebir eşleşmiyor. Genelde proje adında tire/altçizgi karmaşası (örn `my-pkg` vs `my_pkg`).

---

## 2. NuGet trusted publishing policy (UI, ~1 dk) — DİKKAT: her repo için ayrı

NuGet trust policy'leri **repository-ID bazlı pin'li** (GitHub repo'nun numeric ID'sine bağlı). **Wildcard repo henüz desteklenmiyor**. Yani: config-core'da var diye notifications'ta çalışmaz; eskiyi düzenleme, **+ Create** ile yeni bir tane ekle. Manage listesinde paket başına bir satır olur.

<https://www.nuget.org/account/trustedpublishing> → **+ Create**

| Field | Değer |
|---|---|
| Policy name | `<NuGetAccountName> GitHub Actions <package-short> Release` |
| Package owner | `<NuGetAccountName>` (Step 0'daki `NUGET_ACCOUNT` ile **case-sensitive** aynı) |
| Repository owner | `<github-org>` |
| Repository | `<github-repo>` ← her paket için farklı |
| Workflow file | `release-dotnet.yml` |
| Environment | `nuget` |

> NuGet ileride wildcard repo desteklerse tek policy'le birden fazla repo kapsanabilir. O güne kadar paket başına 30 saniyelik tekrarlayan UI işi.

**Yaygın hatalar**:

```
##[error]Input required and not supplied: user
```

→ `vars.NUGET_ACCOUNT` boş; Step 0'daki `gh variable set` komutu unutulmuş.

```
Token exchange failed (401): No matching trust policy owned by user 'X' was found.
```

→ "matching" kelimesine dikkat: trust policy var, ama bu **repo + workflow + environment** combosuyla eşleşmiyor. Genellikle başka bir repo için kayıtlı policy var (config-core'unkini gördüğünde değil notifications için yenisini açtığında). Mevcut policy'yi düzenleme — yeni kayıt ekle.

---

## 3. npm — iki yol var, biri seçilir

### Yol A — npm trusted publisher (varsa görünür)

npmjs.com → kişisel hesap **veya** org settings → **Trusted Publishers** sekmesi. Özellik npm tarafından kademeli rollout'ta — her hesapta görünmüyor. Görünüyorsa form değerleri PyPI ile aynı kalıpta:

| Field | Değer |
|---|---|
| Repository owner | `<github-org>` |
| Repository | `<github-repo>` |
| Workflow filename | `release-npm.yml` |
| Environment | `npm` |
| Package | `@<org>/<pkg>` |

Yol A seçilirse `release-npm.yml`'deki publish step'in `env: NODE_AUTH_TOKEN: …` bloğu silinir; OIDC + trusted publisher token'sız auth eder. `id-token: write` permission provenance için kalır.

### Yol B — `NPM_TOKEN` secret (template default, fallback)

Trusted publisher hesapta yoksa veya görmüyorsan — şu anki template default'u budur:

1. npmjs.com → Account → **Access Tokens** → **Generate new token** → "Automation" tipi → kapsamı `@<org>/*` paketleri.
2. Token'ı kopyala — **chat/log'a yapıştırma**.
3. Step 0'daki komutla repo secret olarak ekle (`gh secret set NPM_TOKEN`).
4. Default `release-npm.yml` zaten token-auth'a göre kurulu:

```yaml
# .github/workflows/release-npm.yml — publish step
- name: publish to npm with provenance
  # Hybrid auth:
  #   - Registry auth: NPM_TOKEN secret (org-member publish token)
  #   - Provenance: GitHub Actions OIDC id-token signs the publish on
  #     sigstore (id-token: write permission MUST stay).
  # Sonuç: paketin npmjs.com sayfasında "GitHub Actions" rozeti görünür,
  # token-auth ile bile provenance kaybolmaz.
  run: npm publish --provenance --access public
  env:
    NODE_AUTH_TOKEN: ${{ secrets.NPM_TOKEN }}
```

5. **90 gün takvim hatırlatıcısı kur**. npm Automation tokenları 90 günde expire — süre dolduğunda yeni token üret + aynı `gh secret set NPM_TOKEN` komutuyla overwrite et. Workflow değişmez.

> Trusted publisher hesabınızda görünür hale gelirse Yol A'ya geç — token rotasyonu derdi biter, leak yüzeyi kapanır.

**Yaygın hata**:

```
404 Not Found - PUT https://registry.npmjs.org/@<org>%2f<pkg>
```

→ `NODE_AUTH_TOKEN` `GITHUB_TOKEN`'a set edilmiş ya da `NPM_TOKEN` secret'ı eksik / expire olmuş.

---

## 4. Token leak güvenlik kuralı

npm/PyPI/NuGet token'ını **asla** Slack / chat / commit / log'a yapıştırma. Yapıştırırsan:

1. Hemen registry UI'dan **revoke** et.
2. Yeni token üret.
3. Sadece `gh secret set ... --body` (lokal terminal) ya da repo Settings UI üzerinden ekle.

PyPI ve NuGet OIDC trusted publishing kullanıyor — token üretmen gerekmez, leak riski yok. Sadece npm bootstrap'inde token gerekiyor (Yol B).

---

## 5. Tag-push flow (sıralı, 0-3 hep tamam olduktan sonra)

```bash
# release/<version> branch merge edildikten sonra:
git switch main && git pull --ff-only

git tag -s -m "<package-name> v<X.Y.Z>" v<X.Y.Z>
git push origin v<X.Y.Z>

# 3 workflow paralel tetiklenir; release-gate all-or-nothing izler.
gh run list --repo <github-org>/<github-repo> --limit 5
```

**Sadece registry-side fix gerekiyorsa** (örn NuGet trust policy unutulmuş) tag'i delete + retag etmeye gerek yok — registry tarafında düzeltip ilgili workflow'u re-run et:

```bash
gh run rerun <run-id> --repo <github-org>/<github-repo> --failed
```

**Workflow KODU değişti** (örn `release-npm.yml`'de `NODE_AUTH_TOKEN` düzeltmesi) ise tag-trigger'lı workflow tag commit'indeki `.github/workflows/*.yml`'i çalıştırır, main'deki güncel kodu değil. Bu durumda delete + retag zorunlu:

```bash
git tag -d v<X.Y.Z>
git push origin :refs/tags/v<X.Y.Z>
git tag -s -m "<package-name> v<X.Y.Z>" v<X.Y.Z> <yeni-commit-sha>
git push origin v<X.Y.Z>
```

> Önemli: paket henüz hiçbir registry'ye publish olmadıysa retag güvenli. Bir registry'ye publish olduktan sonra retag etme — versiyon yakıldı, 0.1.1'e bump et.

---

## 6. Smoke verify (paketler portallarda görünür görünmez)

```bash
# PyPI — JSON API en hızlısı, pip dry-run da olur
curl -s "https://pypi.org/pypi/<pypi-package-name>/json" \
  | python3 -c "import sys, json; d=json.load(sys.stdin); print(d['info']['version'])"

# NuGet
curl -s "https://api.nuget.org/v3-flatcontainer/<nugetaccount>.<packageid>/index.json" \
  | python3 -c "import sys, json; d=json.load(sys.stdin); print(d['versions'][-1])"

# npm
npm view @<org>/<pkg>@<X.Y.Z> version
```

CDN propagation için 1-2 dk gecikme normal — özellikle npm view 404 gelirse paniğe kapılma, workflow log'unda "Publishing to https://registry.npmjs.org" ve `+ @<org>/<pkg>@<X.Y.Z>` satırları varsa publish gerçekten oldu, sadece edge cache henüz cold.

---

## 7. Tek-bakışta hata-çözüm tablosu

| Hata | Sebep | Çözüm |
|---|---|---|
| PyPI `400 Non-user identities cannot create new projects` | Pending publisher yok ya da combo eşleşmiyor | Step 1'i tekrar et, proje adı `pyproject.toml` ile birebir mi kontrol et |
| NuGet `Input required and not supplied: user` | `NUGET_ACCOUNT` repo variable boş | Step 0 — `gh variable set NUGET_ACCOUNT` |
| NuGet `Token exchange failed (401): No matching trust policy` | Trust policy başka repo için kayıtlı | Step 2 — bu repo için **yeni** policy ekle (eskiyi düzenleme) |
| npm `404 Not Found - PUT @<org>%2f<pkg>` | `NODE_AUTH_TOKEN: ${{ secrets.GITHUB_TOKEN }}` veya `NPM_TOKEN` secret eksik / expire | Step 3 / Yol B — `NPM_TOKEN` secret + workflow'da `NODE_AUTH_TOKEN: ${{ secrets.NPM_TOKEN }}`. Token expire olmuşsa 90 günlük rotasyon. |
| Workflow log "publish OK" ama `npm view` 404 | CDN propagation gecikmesi | 1-2 dk bekle, sonra tekrar dene |
| Tag push'tan sonra workflow eski kodla çalışıyor | Workflow değişikliği tag commit'ine girmemiş | Delete + retag (Step 5'in sonu) |
