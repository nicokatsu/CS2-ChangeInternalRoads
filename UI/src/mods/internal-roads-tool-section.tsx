import { bindValue, trigger, useValue } from "cs2/api";
import { ModuleRegistryExtend } from "cs2/modding";
import { Children, cloneElement, isValidElement, ReactElement, ReactNode } from "react";
import internalRoadsIcon from "../imgs/internal-roads.svg";
import { VanillaComponentResolver } from "./VanillaComponentResolver";

type ExtendableComponentResult = {
    props?: {
        children?: ReactNode;
    };
} & ReactElement;

const group = "changeInternalRoads";
const internalRoadsEnabled$ = bindValue<boolean>(group, "internalRoadsEnabled", false);
const showInternalRoadsToggle$ = bindValue<boolean>(group, "showInternalRoadsToggle", false);
const sectionTitle = "Internal Roads";
const tooltipText = "Enable internal road replacement.";

const withAppendedToolSection = (result: ExtendableComponentResult, section: JSX.Element) => {
    if (!isValidElement(result)) {
        return result;
    }

    const nextChildren = [...Children.toArray(result.props?.children), section];
    return cloneElement(result, {
        ...result.props,
        children: nextChildren,
    });
};

const InternalRoadsToolSection = () => {
    const vanilla = VanillaComponentResolver.instance;
    const Section = vanilla?.Section;
    const ToolButton = vanilla?.ToolButton;
    const toolButtonTheme = vanilla?.ToolButtonTheme;
    const focusDisabled = vanilla?.FOCUS_DISABLED;
    const enabled = useValue(internalRoadsEnabled$);
    const show = useValue(showInternalRoadsToggle$);

    if (!show || !Section || !ToolButton) {
        return null;
    }

    return (
        <Section title={sectionTitle}>
            <ToolButton
                src={internalRoadsIcon}
                selected={enabled}
                focusKey={focusDisabled}
                className={toolButtonTheme?.ToolButton}
                tooltip={tooltipText}
                onSelect={() => trigger(group, "setInternalRoadsEnabled", !enabled)}
            />
        </Section>
    );
};

export const InternalRoadsMouseToolOptions: ModuleRegistryExtend = (Component: any) => {
    return (props) => {
        const result = Component(props) as ExtendableComponentResult;
        return withAppendedToolSection(result, <InternalRoadsToolSection />);
    };
};
