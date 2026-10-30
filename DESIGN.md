---
name: AbleBounce
description: A bright native Windows workbench for preparing Live delivery variants.
colors:
  canvas: "#F6F5F0"
  ink: "#182321"
  muted: "#58635C"
  divider: "#DBDED5"
  deep-green: "#183B32"
  acid-yellow: "#D7F36B"
  track-pane: "#F0F1EA"
  selected-row: "#EFF3DE"
  reassurance: "#E7ECE1"
  reassurance-text: "#354B3C"
  white: "#FFFFFF"
  lead-text: "#A43F2E"
  backing-text: "#39725F"
  unresolved-text: "#8B642B"
  checkbox-border: "#7C877F"
  footer-summary: "#E2F0E2"
  footer-status: "#C8D9CB"
  footer-button-text: "#ECF4EC"
  footer-button-border: "#59766B"
typography:
  headline:
    fontFamily: "Segoe UI Variable, Segoe UI"
    fontSize: "26px"
    fontWeight: 600
  title:
    fontFamily: "Segoe UI Variable, Segoe UI"
    fontSize: "19px"
    fontWeight: 600
  body:
    fontFamily: "Segoe UI Variable, Segoe UI"
    fontSize: "14px"
    fontWeight: 400
  label:
    fontFamily: "Segoe UI Variable, Segoe UI"
    fontSize: "12px"
    fontWeight: 400
  caption:
    fontFamily: "Segoe UI Variable, Segoe UI"
    fontSize: "11px"
    fontWeight: 400
rounded:
  control: "7px"
  checkbox: "4px"
spacing:
  small: "8px"
  medium: "12px"
  section: "18px"
  pane: "20px"
  top: "24px"
  shell: "26px"
components:
  button-primary:
    backgroundColor: "{colors.acid-yellow}"
    textColor: "{colors.ink}"
    rounded: "{rounded.control}"
    padding: "9px 15px"
  button-secondary:
    backgroundColor: "{colors.white}"
    textColor: "{colors.ink}"
    rounded: "{rounded.control}"
    padding: "9px 15px"
  input:
    padding: "9px 10px"
  delivery-row-selected:
    backgroundColor: "{colors.selected-row}"
    padding: "4px 10px"
  checkbox-selected:
    backgroundColor: "{colors.acid-yellow}"
    rounded: "{rounded.checkbox}"
    size: "19px"
---

# Design System: AbleBounce

## Overview

AbleBounce is a bright, compact session-delivery workbench. Deep green anchors the shell, acid yellow identifies actionable selection, and ivory keeps long working sessions quiet. Native Windows controls and English task language make the application feel like a practical companion to Live.

**Key Characteristics:**

- Persistent context, compact delivery rows and a visible primary action.
- Flat surfaces, restrained rounding and strong text hierarchy.
- Honest connection, disabled and Demo states.

This document records the implemented WPF system. Frontmatter lengths use portable `px` notation for WPF device-independent units (DIPs); source XAML remains authoritative.

## Colors

### Primary

Deep green anchors the footer, connection status and app mark. Acid yellow marks checked controls and the primary export action; selected rows use the softer selected-row surface.

### Secondary

Lead text uses the darker lead-text token for readable role labels. Backing text uses backing-text; unresolved and inherited roles use unresolved-text. Track-color squares reflect the Live track color, so coral markers may coexist with the darker Lead label. Track colors are data, not a fixed application palette.

### Neutral

Canvas, track-pane and white distinguish the workspace, tree and controls. Ink carries primary text; muted carries descriptions. Divider separates panes and rows. Reassurance has a muted green surface and its own readable text. Footer text and outline tokens provide contrast on deep green.

**The State Has Words Rule.** Pair color with a checkbox, role label, state label or explicit connection text.

## Typography

Segoe UI Variable with Segoe UI fallback is an intentional native Windows tool choice. Body text is regular; project, section and delivery titles use semibold emphasis. The hierarchy is headline for the project, title for pane headings, body for delivery names and controls, label for explanatory copy, and caption for compact metadata. The app name uses a separate semibold 23-DIP treatment.

Long project and track names truncate with ellipses; track names expose a tooltip. Supporting copy wraps. Do not add a display face or a separate monospace system without a product need.

## Layout

The current desktop shell opens at 1280 × 860 DIPs and has a minimum of 1060 × 740. Its header is 78 DIPs and persistent footer 104 DIPs. The main view has a 290-DIP track-role pane, flexible delivery column and 250-DIP export inspector. This is the implemented main-view composition, not a universal layout requirement for future dialogs.

The track tree, delivery list and inspector scroll independently when needed. At the minimum size the delivery column narrows and scrolls; the export action remains visible in the footer. Use the extracted spacing values for recurring gaps; the implementation also uses small local adjustments for dense metadata. Standard buttons and text fields have a 40-DIP minimum height; compact selectors and queue controls intentionally use smaller heights.

## Elevation & Depth

The shell is flat and uses no custom shadows. Subtle surface changes, one-DIP dividers and the deep-green footer establish depth. Native controls retain their platform rendering; do not imitate a floating web dashboard.

## Shapes

Buttons and the reassurance container share gently rounded control corners. Checkboxes are smaller rounded squares. Rows and pane boundaries stay rectangular. The app mark has its own nine-DIP rounded enclosure, with authored WPF path artwork.

## Components

### Buttons

Secondary buttons use white, ink and a divider border. Primary buttons use acid yellow and semibold ink. The export action increases padding to 24 horizontally and 13 vertically, with a 164-DIP minimum width. The cancellation action uses a transparent surface with dedicated footer text and border tokens.

Button hover reduces surface opacity to 0.78. Keyboard focus uses a two-DIP backing-green border. Disabled button surfaces use 0.42 opacity. These are immediate WPF state changes; no custom animation tokens are defined.

### Inputs / Fields

Text fields use divider borders, centered content and deep-green text selection. The output path wraps. Standard combo boxes have a 36-DIP minimum height; track-role selectors use a compact 30-DIP treatment. Preserve native selection, keyboard and menu behavior.

### Track-role tree

Hierarchy is expressed through 13-DIP indentation steps, group detail and track numbering. Each row includes a Live-color square, track name and explicit role selector. Role text uses the semantic colors above. Long content scrolls within the pane.

### Delivery rows

Each of the nine rows has a checkbox, semibold title, explicit vocal/master metadata and a right-aligned state. Selection changes both the checkbox and row surface. Descriptions are available as tooltips. Selection updates the footer count; it does not open an inspector or change the export-settings panel.

### Checkboxes

The 19-DIP box uses a one-DIP outline, acid-yellow checked fill and authored ink tick. Keyboard focus strengthens its deep-green border to two DIPs. Disabled checkboxes use 0.45 opacity.

### Status and supporting surfaces

The footer keeps summary, status, cancellation and export together. A compact reassurance surface sits below the delivery list. Setup opens native menu commands; export calibration is a specific implementation flow, not a global visual pattern. Synthetic sessions must be visibly marked Demo.

## Do's and Don'ts

### Do:

- **Do** keep the primary export action visible as content scrolls.
- **Do** pair state colors with explicit labels or control state.
- **Do** preserve the native Segoe UI hierarchy and keyboard behavior.
- **Do** identify synthetic content as Demo and show connection state plainly.

### Don't:

- **Don't** replace this working application with dashboard metrics, decorative meters or marketing illustrations.
- **Don't** infer role solely from a track-color marker.
- **Don't** add continuous animation or decorative elevation to the flat shell.
- **Don't** present visual review or Demo behavior as evidence of Live integration.
