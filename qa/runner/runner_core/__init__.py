"""SBPR T022 QA runner — M5 orchestration layer (ADR-0009 §1, §5.1, §6, §8, §10).

This package is the **runner brain** that wraps the adopted transport-neutral FSM
(`qa/runner/fsm/`) into the deterministic external T022 runner ADR-0009 authorizes.
The FSM is the scenario state machine + no-false-PASS verdict core; this layer adds
the operational envelope the ADR requires around it:

  * an **exclusive lane lease** (§5.3 owner-local, one run at a time),
  * an **immutable 6-part artifact pin manifest** (§5.1/§8 product/helper/game/
    BepInEx/Harmony/scenario) verified before anything arms,
  * **per-phase timeout budgets** on top of the FSM's single global deadline (§3.2),
  * **evidence-document composition** correlating the run into one artifact (§6),
  * **final verdict authority** — the runner is the SOLE PASS emitter (§6): a PASS
    requires the FSM PASS **and** a held lease **and** verified pins **and** a
    correlated evidence document. Any one missing forces FAIL.

ENGINE-FREE / DRY-RUN ONLY. Nothing here performs game, network, or file I/O. The
`simulation` module scripts every path (success, each failure mode, timeout, crash,
drift, cleanup-crash, evidence tamper, competing lease) through the deterministic
`FakeTransport`, so the full runner is exercised without a live world. A *live*
two-client cold run is the separate operator-authorized **M6** card — never here.
"""
from __future__ import annotations

from .evidence import EvidenceDocument
from .lease import LaneLease, LaneLeaseError
from .live_composition import (
    LiveOperatorEnvironment,
    LiveQualificationPlan,
    LiveRunReport,
    RealOperatorConfig,
    build_live_run,
    real_operator_environment,
    run_live_qualification,
)
from .live_preflight import (
    LiveModeRefused,
    LivePreflightResult,
    evaluate_live_preflight,
)
from .live_transport import (
    ChannelEndpoint,
    EntitlementControlChannel,
    EntitlementDeliveryConfig,
    LiveLoopbackTransport,
    LiveReceiptAdapter,
    LiveRunConfig,
)
from .manifest import ArtifactPinManifest, PinDriftError, RunManifestError
from .operator_drivers import (
    AdminlistGuard,
    ClientSpec,
    DualClientLauncher,
    EntitlementSeeder,
    LaneLauncher,
    LaneSpec,
    OperatorSafetyError,
    SeedResult,
)
from .orchestrator import RunnerVerdict, T022RunOrchestrator
from .timeouts import PhaseBudget, PhaseTimeoutError, PhaseTimeoutTransport

__all__ = [
    "AdminlistGuard",
    "ArtifactPinManifest",
    "ChannelEndpoint",
    "ClientSpec",
    "DualClientLauncher",
    "EntitlementControlChannel",
    "EntitlementDeliveryConfig",
    "EntitlementSeeder",
    "EvidenceDocument",
    "LaneLauncher",
    "LaneLease",
    "LaneLeaseError",
    "LaneSpec",
    "LiveLoopbackTransport",
    "LiveModeRefused",
    "LiveOperatorEnvironment",
    "LivePreflightResult",
    "LiveQualificationPlan",
    "LiveReceiptAdapter",
    "LiveRunConfig",
    "LiveRunReport",
    "OperatorSafetyError",
    "PhaseBudget",
    "PhaseTimeoutError",
    "PhaseTimeoutTransport",
    "PinDriftError",
    "RealOperatorConfig",
    "RunManifestError",
    "RunnerVerdict",
    "SeedResult",
    "T022RunOrchestrator",
    "build_live_run",
    "evaluate_live_preflight",
    "real_operator_environment",
    "run_live_qualification",
]
