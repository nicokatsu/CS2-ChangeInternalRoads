import { ModuleRegistry } from "cs2/modding";
import { ReactNode } from "react";

type SectionProps = {
    title?: ReactNode;
    uiTag?: string;
    children?: ReactNode;
};

type ToolButtonProps = {
    focusKey?: unknown;
    src?: string;
    selected?: boolean;
    disabled?: boolean;
    tooltip?: ReactNode | null;
    uiTag?: string;
    className?: string;
    children?: ReactNode;
    onSelect?: (value: unknown) => unknown;
};

const registryIndex = {
    Section: ["game-ui/game/components/tool-options/mouse-tool-options/mouse-tool-options.tsx", "Section"],
    ToolButton: ["game-ui/game/components/tool-options/tool-button/tool-button.tsx", "ToolButton"],
    ToolButtonTheme: ["game-ui/game/components/tool-options/tool-button/tool-button.module.scss", "classes"],
    FOCUS_DISABLED: ["game-ui/common/focus/focus-key.ts", "FOCUS_DISABLED"],
};

export class VanillaComponentResolver {
    public static get instance(): VanillaComponentResolver | undefined {
        return this.current;
    }

    private static current?: VanillaComponentResolver;

    public static setRegistry(registry: ModuleRegistry) {
        this.current = new VanillaComponentResolver(registry);
    }

    private readonly registryData: ModuleRegistry;
    private readonly cachedData: Partial<Record<keyof typeof registryIndex, any>> = {};

    constructor(registry: ModuleRegistry) {
        this.registryData = registry;
    }

    private updateCache(entry: keyof typeof registryIndex) {
        const entryData = registryIndex[entry];
        return (this.cachedData[entry] = this.registryData.registry.get(entryData[0])?.[entryData[1]]);
    }

    public get Section(): ((props: SectionProps) => JSX.Element) | undefined {
        return this.cachedData.Section ?? this.updateCache("Section");
    }

    public get ToolButton(): ((props: ToolButtonProps) => JSX.Element) | undefined {
        return this.cachedData.ToolButton ?? this.updateCache("ToolButton");
    }

    public get ToolButtonTheme(): { ToolButton?: string } | undefined {
        return this.cachedData.ToolButtonTheme ?? this.updateCache("ToolButtonTheme");
    }

    public get FOCUS_DISABLED(): unknown {
        return this.cachedData.FOCUS_DISABLED ?? this.updateCache("FOCUS_DISABLED");
    }
}
