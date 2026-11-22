<#
  Backup do banco do Atlas Literário (PostgreSQL do docker compose).
  Uso (na raiz do projeto, com `docker compose up -d` rodando):   .\scripts\backup-db.ps1
  Gera backups\atlas-AAAA-MM-DD_HHmm.dump (formato custom do pg_dump). A pasta backups\ não é versionada.

  Restaurar (substitui o conteúdo atual do banco!):
    docker compose cp backups\atlas-XXXX.dump db:/tmp/restore.dump
    docker compose exec -T db pg_restore -U atlas -d atlas_literario --clean --if-exists /tmp/restore.dump
  Se mudou POSTGRES_USER no .env, use o mesmo usuário aqui.
#>
$ErrorActionPreference = 'Stop'
$user = if ($env:POSTGRES_USER) { $env:POSTGRES_USER } else { 'atlas' }
$stamp = Get-Date -Format 'yyyy-MM-dd_HHmm'
New-Item -ItemType Directory -Force backups | Out-Null
$out = "backups/atlas-$stamp.dump"
docker compose exec -T db pg_dump -U $user -Fc atlas_literario -f /tmp/atlas.dump
docker compose cp db:/tmp/atlas.dump $out
docker compose exec -T db rm -f /tmp/atlas.dump
$size = [math]::Round((Get-Item $out).Length / 1KB)
Write-Host "Backup salvo em $out ($size KB)"
