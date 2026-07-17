using System;
using System.Collections.Generic;
using System.Linq;

namespace SBPR.Niflheim.HomesteadStones.Domain.Accounts
{
    // IAP-003 Tracer 1 — pilot disclosure + data-inventory basis (engine-free CLEAN-side core).
    //
    // AIP-FR-002/025: before first account creation the player must acknowledge a concise disclosure
    // that enumerates stored categories, purposes, retention, operator contact, export/deletion route,
    // and the possibility of explicit unreleased-data reset. Acknowledgement records transparency ONLY —
    // it is NOT the selected lawful basis (AT-AIP-DATA-INVENTORY-BASIS): a responsible human documents a
    // lawful-basis position per data category, and the software never selects a legal basis automatically.
    //
    // net48 audit: System.* + LINQ only. No UnityEngine/Valheim/BepInEx.

    /// <summary>One persisted data category, its purpose, retention, access role, recipients, deletion
    /// path, and the human-approved lawful-basis position. The basis is authored, never auto-derived
    /// (GetPilotPrivacyInventory contract).</summary>
    public sealed class PrivacyInventoryCategory
    {
        public PrivacyInventoryCategory(
            string category, string purpose, string retention, string accessRole,
            string recipients, string deletionPath, string lawfulBasisPosition, bool humanApprovedBasis)
        {
            Category = category ?? string.Empty;
            Purpose = purpose ?? string.Empty;
            Retention = retention ?? string.Empty;
            AccessRole = accessRole ?? string.Empty;
            Recipients = recipients ?? string.Empty;
            DeletionPath = deletionPath ?? string.Empty;
            LawfulBasisPosition = lawfulBasisPosition ?? string.Empty;
            HumanApprovedBasis = humanApprovedBasis;
        }

        public string Category { get; }
        public string Purpose { get; }
        public string Retention { get; }
        public string AccessRole { get; }
        public string Recipients { get; }
        public string DeletionPath { get; }
        public string LawfulBasisPosition { get; }

        /// <summary>True only when a responsible human recorded the lawful-basis position for this
        /// category. Software never sets this by itself.</summary>
        public bool HumanApprovedBasis { get; }

        public bool IsComplete =>
            !string.IsNullOrEmpty(Category) && !string.IsNullOrEmpty(Purpose) &&
            !string.IsNullOrEmpty(Retention) && !string.IsNullOrEmpty(AccessRole) &&
            !string.IsNullOrEmpty(DeletionPath) &&
            !string.IsNullOrEmpty(LawfulBasisPosition) && HumanApprovedBasis;
    }

    /// <summary>The static, operator-authored privacy inventory. It is the single source for the player
    /// disclosure and its verification tests; it is not generated from runtime reflection and cannot let
    /// software choose a legal basis (GetPilotPrivacyInventory contract).</summary>
    public sealed class PilotPrivacyInventory
    {
        private readonly List<PrivacyInventoryCategory> _categories;

        public PilotPrivacyInventory(IEnumerable<PrivacyInventoryCategory> categories, string operatorContact, string noticeVersion)
        {
            _categories = new List<PrivacyInventoryCategory>(categories ?? Enumerable.Empty<PrivacyInventoryCategory>());
            OperatorContact = operatorContact ?? string.Empty;
            NoticeVersion = noticeVersion ?? string.Empty;
        }

        public IReadOnlyList<PrivacyInventoryCategory> Categories => _categories;
        public string OperatorContact { get; }
        public string NoticeVersion { get; }

        /// <summary>The categories missing a human-approved lawful-basis position. A non-empty result
        /// blocks enrollment (AT-AIP-DATA-INVENTORY-BASIS).</summary>
        public IReadOnlyList<PrivacyInventoryCategory> CategoriesMissingBasis() =>
            _categories.Where(c => !c.HumanApprovedBasis || string.IsNullOrEmpty(c.LawfulBasisPosition)).ToList();

        /// <summary>The inventory is a valid enrollment basis only when it lists at least one category,
        /// names an operator contact + notice version, and every category is complete with a
        /// human-approved lawful-basis position. An empty inventory is never a pass.</summary>
        public bool IsValidEnrollmentBasis() =>
            _categories.Count > 0 &&
            !string.IsNullOrEmpty(OperatorContact) &&
            !string.IsNullOrEmpty(NoticeVersion) &&
            _categories.All(c => c.IsComplete);
    }

    /// <summary>The concise player disclosure required before first account creation (AIP-FR-002/025).
    /// It enumerates the stored categories, purpose, retention, operator contact, export/deletion route,
    /// and the explicit unreleased-data-reset possibility. Built from the authored inventory so the
    /// disclosure and the inventory can never silently diverge.</summary>
    public sealed class PilotDisclosure
    {
        private readonly PilotPrivacyInventory _inventory;

        public PilotDisclosure(PilotPrivacyInventory inventory, string exportDeletionRoute, bool statesExplicitResetPossibility)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            ExportDeletionRoute = exportDeletionRoute ?? string.Empty;
            StatesExplicitResetPossibility = statesExplicitResetPossibility;
        }

        public string NoticeVersion => _inventory.NoticeVersion;
        public string OperatorContact => _inventory.OperatorContact;
        public string ExportDeletionRoute { get; }

        /// <summary>Whether the disclosure explicitly states unreleased data may be reset (spec US5.1).</summary>
        public bool StatesExplicitResetPossibility { get; }

        public IReadOnlyList<string> StoredCategoryNames() =>
            _inventory.Categories.Select(c => c.Category).ToList();

        /// <summary>Every mandatory disclosure element is present (AT-AIP-DISCLOSURE-COMPLETE): stored
        /// categories, purposes, retention, operator contact, export/deletion route, explicit-reset
        /// statement, AND the underlying inventory is a valid, human-approved-basis enrollment basis.</summary>
        public IReadOnlyList<string> MissingElements()
        {
            var missing = new List<string>();
            if (_inventory.Categories.Count == 0) missing.Add("stored-categories");
            if (_inventory.Categories.Any(c => string.IsNullOrEmpty(c.Purpose))) missing.Add("purpose");
            if (_inventory.Categories.Any(c => string.IsNullOrEmpty(c.Retention))) missing.Add("retention");
            if (string.IsNullOrEmpty(OperatorContact)) missing.Add("operator-contact");
            if (string.IsNullOrEmpty(ExportDeletionRoute)) missing.Add("export-deletion-route");
            if (!StatesExplicitResetPossibility) missing.Add("explicit-reset-possibility");
            if (string.IsNullOrEmpty(NoticeVersion)) missing.Add("notice-version");
            foreach (var c in _inventory.CategoriesMissingBasis())
                missing.Add("lawful-basis:" + c.Category);
            return missing;
        }

        public bool IsComplete() => MissingElements().Count == 0 && _inventory.IsValidEnrollmentBasis();
    }

    /// <summary>The record that a player acknowledged a specific disclosure version at a coarse time.
    /// This is transparency evidence only; it is NOT the lawful basis (AIP-FR-002; data-model.md
    /// Aggregate 0 note). The account resolver requires BOTH a complete disclosure (human-approved
    /// basis) AND this acknowledgement before minting an account.</summary>
    public readonly struct DisclosureAcknowledgement
    {
        public DisclosureAcknowledgement(string noticeVersion, long acknowledgedAtUnixSeconds)
        {
            NoticeVersion = noticeVersion ?? string.Empty;
            AcknowledgedAtUnixSeconds = acknowledgedAtUnixSeconds;
        }

        public string NoticeVersion { get; }
        public long AcknowledgedAtUnixSeconds { get; }
        public bool IsPresent => !string.IsNullOrEmpty(NoticeVersion) && AcknowledgedAtUnixSeconds > 0;

        /// <summary>True when this acknowledgement matches the required notice version. A stale/absent
        /// acknowledgement does not satisfy the disclosure gate.</summary>
        public bool Satisfies(string requiredNoticeVersion) =>
            IsPresent && string.Equals(NoticeVersion, requiredNoticeVersion, StringComparison.Ordinal);
    }
}
