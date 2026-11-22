<#
.SYNOPSIS
  Promove/rebaixa uma conta por e-mail. Não existe endpoint de API para isso de propósito (e-mails não são verificados).
.EXAMPLE
  ./scripts/promote-moderator.ps1 -Email pessoa@exemplo.com            # promove a Moderator
  ./scripts/promote-moderator.ps1 -Email pessoa@exemplo.com -Role User # rebaixa
.NOTES
  SQL via psql: se a variável de ambiente PSQL_URL existir, usa `psql $PSQL_URL`; senão usa `docker compose exec db psql`
  (serviço "db" do docker-compose.yml da raiz, usuário POSTGRES_USER/atlas, banco atlas_literario).
  O papel vale imediatamente para as permissões (lido do banco); na tela, após novo login ou refresh do token.
#>
param(
  [Parameter(Mandatory = $true)][string]$Email,
  [ValidateSet('Moderator', 'User')][string]$Role = 'Moderator'
)
$ErrorActionPreference = 'Stop'
$email = $Email.Trim().ToLowerInvariant()
$roleValue = if ($Role -eq 'Moderator') { 1 } else { 0 }
$sql = 'UPDATE "user" SET role = :role, updated_at = now() WHERE email = :''email'' RETURNING email, role;'

if ($env:PSQL_URL) {
  $result = $sql | psql $env:PSQL_URL -X -q -v ON_ERROR_STOP=1 -v "email=$email" -v "role=$roleValue" -At -F ' => '
} else {
  Push-Location (Join-Path $PSScriptRoot '..\..')
  try {
    $result = $sql | docker compose exec -T db sh -c 'psql -U "${POSTGRES_USER:-atlas}" -d atlas_literario -X -q -v ON_ERROR_STOP=1 -v email="$0" -v role="$1" -At -F " => "' $email $roleValue
  } finally { Pop-Location }
}
if (-not $result) { Write-Error "Nenhuma conta com o e-mail $email."; exit 1 }
Write-Host "OK: $result  (papel solicitado: $Role)"
