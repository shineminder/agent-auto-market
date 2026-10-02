# agent_serveur_signal.ps1 - chaque autorisation est liee a un signal pris par l agent ; ecart grave = pause (lot3)
$ErrorActionPreference = 'Stop'
$php  = 'E:\php\php-8.2.34\php.exe'
$date = Get-Date -Format 'yyyyMMdd_HHmmss'
$utf8 = New-Object Text.UTF8Encoding $false
if (-not (Test-Path .\artisan)) { Write-Host 'Arret : lancer depuis la racine du projet (artisan introuvable).' -ForegroundColor Red; exit 1 }
$fx = 'App\Agent\Api\Executions.php'
$fc = 'App\Http\Controllers\Api\AgentApiController.php'
foreach ($f in $fx, $fc) { if (-not (Test-Path $f)) { Write-Host "Arret : introuvable $f" -ForegroundColor Red; exit 1 } }
$x = [IO.File]::ReadAllText((Resolve-Path $fx))
$c = [IO.File]::ReadAllText((Resolve-Path $fc))
if ($x.Contains('signalDeLaDemande')) { Write-Host 'Deja fait.' -ForegroundColor Green; exit 0 }
$nlx = if ($x.Contains("`r`n")) { "`r`n" } else { "`n" }
$nlc = if ($c.Contains("`r`n")) { "`r`n" } else { "`n" }
$script:erreurs = 0

function N([string]$s, [string]$nl) { ($s -replace "`r`n", "`n") -replace "`n", $nl }
function Unique([string]$txt, [string]$motif, [string]$nom) {
    $n = ([regex]::Matches($txt, [regex]::Escape($motif))).Count
    if ($n -ne 1) { Write-Host "Repere introuvable ou multiple ($n) : $nom" -ForegroundColor Red; $script:erreurs++; return $false }
    return $true
}
function Remplacer([string]$txt, [string]$ancien, [string]$nouveau, [string]$nl, [string]$nom) {
    if (-not (Unique $txt $ancien $nom)) { return $txt }
    return $txt.Replace($ancien, (N $nouveau $nl))
}
function Avant([string]$txt, [string]$repere, [string]$ajout, [string]$nl, [string]$nom) {
    if (-not (Unique $txt $repere $nom)) { return $txt }
    $i = $txt.LastIndexOf("`n", $txt.IndexOf($repere)) + 1
    return $txt.Substring(0, $i) + (N $ajout $nl) + $nl + $txt.Substring($i)
}
function Apres([string]$txt, [string]$repere, [string]$ajout, [string]$nl, [string]$nom) {
    if (-not (Unique $txt $repere $nom)) { return $txt }
    $i = $txt.IndexOf($repere); $fin = $txt.IndexOf("`n", $i)
    return $txt.Substring(0, $fin + 1) + (N $ajout $nl) + $nl + $txt.Substring($fin + 1)
}

# ==== Executions.php ==========================================================================
$x = Avant $x '        // Evaluation et reservation sous verrou' @'
        // Lien obligatoire avec un signal pris par cet agent (lot3) : l appareil n est pas fiable.
        $signal = null;
        $dejaLie = false;
        if (Actions::engageDesFonds($demande->action)) {
            [$signal, $motifSignal, $grave] = $this->signalDeLaDemande($agent, $d, $demande);
            if ($motifSignal) {
                return $this->refusSignal($agent, $appareil, $correlation, $motifSignal, $grave);
            }
        }

'@ $nlx 'debut de controle'

$x = Remplacer $x 'DB::transaction(function () use ($agent, $appareil, $demande, $correlation) {' 'DB::transaction(function () use ($agent, $appareil, $demande, $correlation, $signal, &$dejaLie) {' $nlx 'transaction'

$x = Apres $x '            Agent::whereKey($agent->id)->lockForUpdate()->first();' @'

            // Sous verrou : un signal ne sert qu a une seule autorisation valable
            if ($signal && $this->signalDejaLie((int) $signal->id)) {
                $dejaLie = true;
                return [null, null];
            }
'@ $nlx 'verrou'

$x = Avant $x '        if (!$decision->autorise) {' @'
        if ($dejaLie) {
            return $this->refusSignal($agent, $appareil, $correlation, 'signal_utilise', false);
        }

'@ $nlx 'apres transaction'

$x = Apres $x @'
                'appareil_id' => $appareil->id,
'@ @'
                'signal_id'   => $signal ? $signal->id : null,
'@ $nlx 'creation autorisation'

$x = Remplacer $x @'
                    'signal_id'       => $d['signal_id'] ?? null,
'@ @'
                    'signal_id'       => $a->signal_id,
'@ $nlx 'enregistrer signal_id'

$x = Remplacer $x 'if (!empty($d[''signal_id''])) {' 'if (!empty($e->signal_id)) {' $nlx 'enregistrer test signal'
$x = Remplacer $x '->whereKey($d[''signal_id''])' '->whereKey($e->signal_id)' $nlx 'enregistrer mise a jour signal'

$x = Avant $x '        if (($d[''action''] ?? null) !== $a->action) {' @'
        if (!empty($d['signal_id']) && $a->signal_id && (int) $d['signal_id'] !== (int) $a->signal_id) {
            return 'autorisation_signal';
        }

'@ $nlx 'controler'

$x = Avant $x '    private function echeance(AgentExecution $a): Carbon' @'
    /**
     * Signal cite par la demande (lot3). Renvoie [signal, motif, grave] : motif null si la demande
     * correspond exactement a un signal pris par cet agent ; grave = ecart qui ne peut venir que d un appareil modifie.
     */
    private function signalDeLaDemande(Agent $agent, array $d, Demande $demande): array
    {
        $id = (int) ($d['signal_id'] ?? 0);
        if ($id <= 0) {
            return [null, 'signal_requis', false];
        }

        $s = AgentSignal::whereKey($id)->first();
        if (!$s) {
            return [null, 'signal_inconnu', false];
        }
        if ((int) $s->agent_id !== (int) $agent->id) {
            return [null, 'signal_etranger', true];
        }
        if ($s->etat !== Etats::PRIS) {
            return [null, 'signal_indisponible', false];
        }
        if ($s->expire_le && $s->expire_le->isPast()) {
            return [null, 'signal_expire', false];
        }
        if ($demande->action !== $s->action) {
            return [null, 'signal_action', true];
        }
        if (strtoupper(trim((string) $demande->symbol)) !== strtoupper((string) $s->symbol)) {
            return [null, 'signal_symbole', true];
        }
        if ($s->plateforme && $demande->plateforme && strcasecmp((string) $demande->plateforme, (string) $s->plateforme) !== 0) {
            return [null, 'signal_plateforme', true];
        }
        if (strtoupper((string) $demande->devise) !== strtoupper((string) ($s->devise ?: 'USD'))) {
            return [null, 'signal_devise', true];
        }
        if ((float) $demande->montant > (float) $s->montant + 0.01) {
            return [null, 'signal_montant', true];
        }

        return [$s, null, false];
    }

    /** Un signal ne sert qu a une autorisation encore valable, ou deja executee. */
    private function signalDejaLie(int $signalId): bool
    {
        foreach (AgentExecution::where('signal_id', $signalId)->get() as $e) {
            if (in_array(self::etatEffectif($e), [Etats::DEMANDE, Etats::A_CONFIRMER, Etats::REUSSIE], true)) {
                return true;
            }
        }

        return false;
    }

    /** Refus lie au signal ; un ecart grave met l agent en pause (arret d urgence) et le journalise. */
    private function refusSignal(Agent $agent, AgentAppareil $appareil, $correlation, string $motif, bool $grave): array
    {
        $message = __('agent.policy_' . $motif);
        if ($message === 'agent.policy_' . $motif) {
            $message = $grave
                ? 'Demande non conforme au signal : agent mis en pause par securite.'
                : 'Demande refusee : elle ne correspond a aucun signal disponible.';
        }

        if ($grave) {
            Agent::whereKey($agent->id)->whereNotIn('etat', Etats::agentTermine())->update(['etat' => Etats::PAUSE]);
            \Log::warning("Agent #{$agent->id} mis en pause : demande non conforme ({$motif}), appareil #{$appareil->id}.");
        }

        $this->journal->refus(Evenements::ACTION_REFUSEE, [
            'user_id'     => $agent->user_id,
            'agent_id'    => $agent->id,
            'appareil_id' => $appareil->id,
            'correlation' => $correlation,
            'donnees'     => ['motif' => $message, 'pause' => $grave],
        ]);

        return ['autorise' => false, 'motif' => $motif, 'message' => $message, 'correlation' => $correlation];
    }

'@ $nlx 'nouvelles methodes'

# ==== AgentApiController.php : signal_id accepte par la validation de /autoriser ===============
$c = Apres $c @'
            'correlation' => 'nullable|string|max:40',
'@ @'
            'signal_id'   => 'nullable|integer',
'@ $nlc 'validation autoriser'

# ==== Ecriture, controles ======================================================================
if ($script:erreurs -gt 0) { Write-Host "Arret : $($script:erreurs) repere(s) non trouve(s), rien n a ete modifie." -ForegroundColor Red; exit 1 }
foreach ($f in $fx, $fc) {
    $d = Join-Path 'storage\sauvegarde' "$date\$f"
    New-Item -ItemType Directory -Force -Path (Split-Path $d) | Out-Null
    Copy-Item $f $d
}
[IO.File]::WriteAllText((Resolve-Path $fx), $x, $utf8)
[IO.File]::WriteAllText((Resolve-Path $fc), $c, $utf8)
Write-Host "Sauvegarde : storage\sauvegarde\$date"
foreach ($f in $fx, $fc) {
    & $php -l $f
    if ($LASTEXITCODE -ne 0) { Write-Host "Arret : erreur de syntaxe dans $f (sauvegarde : storage\sauvegarde\$date)" -ForegroundColor Red; exit 1 }
}
& $php artisan agent:tester --user=1
if ($LASTEXITCODE -ne 0) { Write-Host 'agent:tester en echec : s il signale signal_requis, il doit etre adapte (envoyez-moi son fichier). NE PAS LIVRER.' -ForegroundColor Yellow; exit 1 }
Write-Host 'Termine : recycler le pool IIS.' -ForegroundColor Green