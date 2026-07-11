namespace LeafLedger.Modules.Ledger.Domain.PostingValidity;

public static class PostingValidityEvaluator
{
    public static PostingValidityError? AssertPostingAccountsValid(
        PostingPurpose purpose,
        IReadOnlyCollection<AccountReference> accounts,
        IReadOnlyCollection<PostingReference> references)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(references);

        var catalog = accounts.ToDictionary(account => account.Id);
        var issues = new List<PostingValidityIssue>();
        foreach (var reference in references)
        {
            var reason = EvaluateAccountReference(purpose, catalog, reference);
            if (reason.HasValue)
            {
                issues.Add(new PostingValidityIssue(
                    PostingEntityType.Account,
                    reference.EntityId,
                    reason.Value,
                    reference.TransactionDate,
                    reference.Source));
            }
        }

        return ToError(issues);
    }

    public static PostingValidityError? AssertPostingBusinessPartnersValid(
        PostingPurpose purpose,
        IReadOnlyCollection<TimeboundReference> businessPartners,
        IReadOnlyCollection<PostingReference> references) =>
        AssertPostingTimeboundReferencesValid(
            purpose,
            PostingEntityType.BusinessPartner,
            businessPartners,
            references);

    public static PostingValidityError? AssertPostingUsersValid(
        PostingPurpose purpose,
        IReadOnlyCollection<TimeboundReference> users,
        IReadOnlyCollection<PostingReference> references) =>
        AssertPostingTimeboundReferencesValid(
            purpose,
            PostingEntityType.User,
            users,
            references);

    private static PostingValidityError? AssertPostingTimeboundReferencesValid(
        PostingPurpose purpose,
        PostingEntityType entityType,
        IReadOnlyCollection<TimeboundReference> catalogItems,
        IReadOnlyCollection<PostingReference> references)
    {
        ArgumentNullException.ThrowIfNull(catalogItems);
        ArgumentNullException.ThrowIfNull(references);
        var catalog = catalogItems.ToDictionary(item => item.Id);
        var issues = new List<PostingValidityIssue>();
        foreach (var reference in references)
        {
            var reason = EvaluateTimeboundReference(purpose, catalog, reference);
            if (reason.HasValue)
            {
                issues.Add(new PostingValidityIssue(
                    entityType,
                    reference.EntityId,
                    reason.Value,
                    reference.TransactionDate,
                    reference.Source));
            }
        }

        return ToError(issues);
    }

    public static PostingValidityError? AssertPostingProjectsValid(
        IReadOnlyCollection<ProjectReference> projects,
        IReadOnlyCollection<PostingReference> references)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(references);

        var catalog = projects.ToDictionary(project => project.Id);
        var issues = new List<PostingValidityIssue>();
        foreach (var reference in references)
        {
            var reason = EvaluateProjectReference(catalog, reference);
            if (reason.HasValue)
            {
                issues.Add(new PostingValidityIssue(
                    PostingEntityType.Project,
                    reference.EntityId,
                    reason.Value,
                    reference.TransactionDate,
                    reference.Source));
            }
        }

        return ToError(issues);
    }

    private static PostingValidityReason? EvaluateAccountReference(
        PostingPurpose purpose,
        Dictionary<Guid, AccountReference> accounts,
        PostingReference reference)
    {
        if (!accounts.TryGetValue(reference.EntityId, out var account))
        {
            return PostingValidityReason.Missing;
        }

        if (!account.IsActive)
        {
            return PostingValidityReason.Inactive;
        }

        return purpose == PostingPurpose.Business
            ? EvaluateWindow(reference.TransactionDate, account.ValidFrom, account.ValidTo)
            : null;
    }

    private static PostingValidityReason? EvaluateTimeboundReference(
        PostingPurpose purpose,
        Dictionary<Guid, TimeboundReference> catalog,
        PostingReference reference)
    {
        if (!catalog.TryGetValue(reference.EntityId, out var item))
        {
            return PostingValidityReason.Missing;
        }

        if (purpose == PostingPurpose.Personal)
        {
            return item.IsActive ? null : PostingValidityReason.Inactive;
        }

        return EvaluateWindow(reference.TransactionDate, item.ValidFrom, item.ValidTo);
    }

    private static PostingValidityReason? EvaluateProjectReference(
        Dictionary<Guid, ProjectReference> projects,
        PostingReference reference)
    {
        if (!projects.TryGetValue(reference.EntityId, out var project))
        {
            return PostingValidityReason.Missing;
        }

        return EvaluateWindow(reference.TransactionDate, project.StartDate, project.EndDate);
    }

    private static PostingValidityReason? EvaluateWindow(
        DateOnly transactionDate,
        DateOnly? validFrom,
        DateOnly? validTo)
    {
        if (validFrom.HasValue && transactionDate < validFrom.Value)
        {
            return PostingValidityReason.Future;
        }

        return validTo.HasValue && transactionDate > validTo.Value
            ? PostingValidityReason.Expired
            : null;
    }

    private static PostingValidityError? ToError(List<PostingValidityIssue> issues) =>
        issues.Count == 0 ? null : new PostingValidityError(issues.AsReadOnly());
}
