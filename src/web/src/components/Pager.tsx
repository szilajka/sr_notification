interface PagerProps {
  page: number;
  pageSize: number;
  totalCount: number;
  onPage: (page: number) => void;
}

export function Pager({ page, pageSize, totalCount, onPage }: PagerProps) {
  const pages = Math.max(1, Math.ceil(totalCount / pageSize));
  if (pages <= 1) return null;
  return (
    <nav className="pager" aria-label="Pages">
      <button type="button" className="button-quiet" disabled={page <= 1} onClick={() => onPage(page - 1)}>
        Previous
      </button>
      <span>
        Page {page} of {pages}
      </span>
      <button type="button" className="button-quiet" disabled={page >= pages} onClick={() => onPage(page + 1)}>
        Next
      </button>
    </nav>
  );
}
