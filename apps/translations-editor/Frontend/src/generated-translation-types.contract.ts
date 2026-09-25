import { m } from "virtual:runic-translations/editor";

const title: string = m.app_title();
const count: string = m.ui_count_messages({ count: 2n }, { locale: "de" });

// @ts-expect-error The generated integer input rejects string carriers.
m.ui_count_messages({ count: "2" });
// @ts-expect-error The generated catalog exposes only exact message keys.
m.not_a_generated_editor_message();

void title;
void count;
