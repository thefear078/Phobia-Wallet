import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetDescription,
} from "@/components/ui/sheet";
import type { ReactNode } from "react";

export function ActionSheet({
  open,
  onOpenChange,
  title,
  description,
  children,
}: {
  open: boolean;
  onOpenChange: (v: boolean) => void;
  title: string;
  description?: string;
  children: ReactNode;
}) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent
        side="bottom"
        className="rounded-t-3xl border-border bg-popover px-5 pb-8 pt-4 max-h-[90vh] overflow-y-auto"
      >
        <div className="mx-auto mb-3 h-1.5 w-10 rounded-full bg-muted" />
        <SheetHeader className="text-left">
          <SheetTitle className="text-lg">{title}</SheetTitle>
          {description && (
            <SheetDescription className="text-xs text-muted-foreground">
              {description}
            </SheetDescription>
          )}
        </SheetHeader>
        <div className="mt-4 space-y-3">{children}</div>
      </SheetContent>
    </Sheet>
  );
}
